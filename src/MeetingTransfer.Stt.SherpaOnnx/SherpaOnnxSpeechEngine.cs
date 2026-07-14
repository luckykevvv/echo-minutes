using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;
using MeetingTransfer.Stt;

namespace MeetingTransfer.Stt.SherpaOnnx;

public sealed partial class SherpaOnnxSpeechEngine : ISpeechEngine
{
    private readonly SherpaOnnxOptions _options;
    private readonly Dictionary<string, SourceAudioBuffer> _buffers = [];
    private readonly Dictionary<string, string> _lastTextBySourceId = [];
    private readonly string _baseDirectory = AppContext.BaseDirectory;
    private int _speakerCounter;

    public SherpaOnnxSpeechEngine(SherpaOnnxOptions options)
    {
        _options = options;
    }

    public string Name => "sherpa-onnx";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var onlineRecognizerExecutable = ResolvePath(_options.OnlineRecognizerExecutable);
        if (string.IsNullOrWhiteSpace(onlineRecognizerExecutable) ||
            !File.Exists(onlineRecognizerExecutable))
        {
            throw new FileNotFoundException(
                "Built-in sherpa-onnx online recognizer is missing. Rebuild or republish the app so models/sherpa-onnx is copied beside the executable.",
                _options.OnlineRecognizerExecutable);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> ProcessAudioAsync(PcmAudioChunk chunk, CancellationToken cancellationToken)
    {
        var buffer = GetBuffer(chunk);
        buffer.Append(chunk);

        if (buffer.Duration < TimeSpan.FromSeconds(4))
        {
            return [];
        }

        if (buffer.IsEffectivelySilent(_options.RealtimeSilenceThresholdDb))
        {
            buffer.Discard();
            return [];
        }

        var wavPath = buffer.FlushToTempWav();
        try
        {
            var onlineRecognizerExecutable = ResolvePath(_options.OnlineRecognizerExecutable)
                ?? throw new InvalidOperationException("Built-in sherpa-onnx online recognizer is missing.");
            var arguments = ApplyTemplate(_options.OnlineArgumentsTemplate, wavPath);
            var output = await RunProcessAsync(onlineRecognizerExecutable, arguments, cancellationToken)
                .ConfigureAwait(false);
            var text = ExtractTranscriptText(output);
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            if (string.Equals(text, _lastTextBySourceId.GetValueOrDefault(chunk.SourceId), StringComparison.Ordinal))
            {
                return [];
            }
            _lastTextBySourceId[chunk.SourceId] = text;

            var speakerId = chunk.SourceKind == AudioSourceKind.Microphone ? "local-user" : "remote-1";
            var speakerName = chunk.SourceKind == AudioSourceKind.Microphone ? "Me" : "Remote";

            return
            [
                new TranscriptSegment
                {
                    SpeakerId = speakerId,
                    SpeakerName = speakerName,
                    SourceId = chunk.SourceId,
                    SourceKind = chunk.SourceKind,
                    Start = buffer.FlushedStart,
                    End = buffer.FlushedEnd,
                    Text = text,
                    IsProvisional = false
                }
            ];
        }
        finally
        {
            TryDelete(wavPath);
        }
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(string wavPath, string sourceId, CancellationToken cancellationToken)
        => await TranscribeFileAsync(wavPath, sourceId, progress: null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(
        string wavPath,
        string sourceId,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var segments = await TranscribeFileCoreAsync(wavPath, sourceId, progress, cancellationToken)
            .ConfigureAwait(false);

        return await ApplySpeakerDiarizationAsync(segments, wavPath, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeFileCoreAsync(
        string wavPath,
        string sourceId,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.Preparing, 5,
            "Preparing transcription..."));
        var whisperCppError = string.Empty;
        if (ShouldPreferWhisperCpp())
        {
            if (TryBuildWhisperCppRequest(wavPath, out var whisperCppExecutable, out var whisperCppArguments, out whisperCppError))
            {
                return await RunWhisperCppAsync(whisperCppExecutable, whisperCppArguments, wavPath, sourceId, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrEmpty(whisperCppError))
        {
            throw new InvalidOperationException(whisperCppError);
        }

        // Prefer a manifest-selected model (from Models\catalog.json) when one is
        // active. This drives Whisper / SenseVoice / Paraformer / Qwen3-ASR from a
        // single code path. Falls back to the legacy Whisper* fields below.
        if (TryBuildManifestRequest(_options.ActiveModelId, wavPath,
                out var manifestExe, out var manifestArgs, out var manifestError))
        {
            return await RunModelAsync(manifestExe, manifestArgs, wavPath, sourceId, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(manifestError))
        {
            throw new InvalidOperationException(manifestError);
        }

        // Prefer Whisper large-v3 when its encoder/decoder/tokens are all present.
        // Whisper gives substantially better accuracy on Chinese and bilingual
        // audio than the streaming paraformer model.
        if (TryBuildWhisperRequest(wavPath, out var whisperExecutable, out var whisperArguments, out var whisperError))
        {
            // sherpa-onnx 1.13.4's Whisper wrapper hard-caps each call to the first
            // 30 seconds of audio (see offline-recognizer-whisper-impl.h:99), so any
            // longer file must be split into overlapping 30 s chunks and stitched
            // back together. Short files (<= 30 s) still go through the single-call
            // path for low overhead.
            var duration = TryProbeAudioDuration(wavPath, out var probedDuration)
                ? probedDuration
                : TimeSpan.FromSeconds(30);

            const int chunkSeconds = 30;
            if (duration > TimeSpan.FromSeconds(chunkSeconds + 1))
            {
                return await ChunkedWhisperTranscribeAsync(
                    whisperExecutable,
                    whisperArguments,
                    wavPath,
                    sourceId,
                    chunkSeconds,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            var whisperOutput = await RunProcessAsync(whisperExecutable, whisperArguments, cancellationToken)
                .ConfigureAwait(false);
            var whisperText = ExtractTranscriptText(whisperOutput);
            if (!string.IsNullOrWhiteSpace(whisperText))
            {
                return
                [
                    new TranscriptSegment
                    {
                        SpeakerId = NextSpeakerId(),
                        SpeakerName = "Speaker 1",
                        SourceId = sourceId,
                        SourceKind = AudioSourceKind.ImportedFile,
                        Start = TimeSpan.Zero,
                        End = duration,
                        Text = whisperText,
                        IsProvisional = false
                    }
                ];
            }
        }
        else if (!string.IsNullOrEmpty(whisperError))
        {
            // Whisper was partially configured but incomplete. Surface a clear
            // error rather than silently falling back to the weaker paraformer.
            throw new InvalidOperationException(whisperError);
        }

        var offlineRecognizerExecutable = ResolvePath(_options.OfflineRecognizerExecutable);
        if (string.IsNullOrWhiteSpace(offlineRecognizerExecutable))
        {
            throw new InvalidOperationException("Built-in sherpa-onnx recognizer is missing. Rebuild or republish the app so models/sherpa-onnx is copied beside the executable.");
        }

        if (!File.Exists(offlineRecognizerExecutable))
        {
            throw new FileNotFoundException(
                "Built-in sherpa-onnx recognizer path does not exist. Rebuild or republish the app so models/sherpa-onnx is copied beside the executable.",
                _options.OfflineRecognizerExecutable);
        }

        var arguments = ApplyTemplate(_options.OfflineArgumentsTemplate, wavPath);
        var output = await RunProcessAsync(offlineRecognizerExecutable, arguments, cancellationToken)
            .ConfigureAwait(false);

        var text = ExtractTranscriptText(output);
        return
        [
            new TranscriptSegment
            {
                SpeakerId = NextSpeakerId(),
                SpeakerName = "Speaker 1",
                SourceId = sourceId,
                SourceKind = AudioSourceKind.ImportedFile,
                Start = TimeSpan.Zero,
                End = TimeSpan.Zero,
                Text = text,
                IsProvisional = false
            }
        ];
    }

    private async Task<IReadOnlyList<TranscriptSegment>> ApplySpeakerDiarizationAsync(
        IReadOnlyList<TranscriptSegment> transcript,
        string wavPath,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (transcript.Count == 0 || !TryBuildDiarizationRequest(wavPath, out var executable, out var arguments))
        {
            progress?.Report(TranscriptionProgress.ForStage(
                TranscriptionStage.Complete, 100, $"{transcript.Count} segment(s)"));
            return transcript;
        }

        progress?.Report(TranscriptionProgress.ForStage(
            TranscriptionStage.Diarizing, 0, "Identifying speakers... 0%"));

        var lastDiarizationPercent = -1.0;
        var output = await RunProcessAsyncWithStderr(executable, arguments, line =>
        {
            if (!TryParseDiarizationProgress(line, out var percent) ||
                percent - lastDiarizationPercent < 0.5)
            {
                return;
            }

            lastDiarizationPercent = percent;
            progress?.Report(TranscriptionProgress.ForStage(
                TranscriptionStage.Diarizing,
                percent,
                $"Identifying speakers... {percent:0}%"));
        }, cancellationToken).ConfigureAwait(false);
        var diarSegments = ParseDiarizationOutput(output);
        if (diarSegments.Count == 0)
        {
            throw new InvalidOperationException(
                "Speaker diarization completed but returned no speaker turns. Check the bundled diarization models.");
        }

        var diarizedTranscript = SplitAndAssignSpeakers(transcript, diarSegments);
        progress?.Report(TranscriptionProgress.ForStage(
            TranscriptionStage.Complete,
            100,
            $"{diarizedTranscript.Count} segment(s), {diarizedTranscript.Select(x => x.SpeakerId).Distinct().Count()} speaker(s)"));
        return diarizedTranscript;
    }

    internal static bool TryParseDiarizationProgress(string line, out double percent)
    {
        var match = Regex.Match(
            line,
            @"\bprogress\s+(?<percent>\d+(?:\.\d+)?)%",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success ||
            !double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            percent = 0;
            return false;
        }

        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    private bool TryBuildDiarizationRequest(string wavPath, out string executable, out string arguments)
    {
        executable = ResolvePath(_options.SpeakerDiarizationExecutable) ?? string.Empty;
        var segmentationModel = ResolvePath(_options.PyannoteSegmentationModel);
        var embeddingModel = ResolvePath(_options.SpeakerEmbeddingModel);

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) ||
            string.IsNullOrWhiteSpace(segmentationModel) || !File.Exists(segmentationModel) ||
            string.IsNullOrWhiteSpace(embeddingModel) || !File.Exists(embeddingModel))
        {
            arguments = string.Empty;
            return false;
        }

        var clustering = _options.DiarizationClusterCount > 0
            ? $"--clustering.num-clusters={_options.DiarizationClusterCount}"
            : $"--clustering.cluster-threshold={_options.DiarizationClusterThreshold.ToString(CultureInfo.InvariantCulture)}";

        arguments = string.Join(' ',
            clustering,
            $"--segmentation.pyannote-model=\"{segmentationModel}\"",
            "--segmentation.num-threads=2",
            $"--embedding.model=\"{embeddingModel}\"",
            "--embedding.num-threads=2",
            $"\"{wavPath}\"");
        return true;
    }

    internal static IReadOnlyList<DiarSegment> ParseDiarizationOutput(string output)
    {
        var result = new List<DiarSegment>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(
                line.Trim(),
                @"^(?<start>\d+(?:\.\d+)?)\s+--\s+(?<end>\d+(?:\.\d+)?)\s+speaker_(?<speaker>\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new DiarSegment(
                double.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["speaker"].Value, CultureInfo.InvariantCulture)));
        }

        return result;
    }

    internal static void AssignSpeakers(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<DiarSegment> diarSegments)
    {
        var speakerMap = BuildContiguousSpeakerMap(diarSegments);
        foreach (var segment in transcript)
        {
            var start = segment.Start.TotalSeconds;
            var end = segment.End.TotalSeconds;
            var best = diarSegments
                .Select(turn => new
                {
                    Turn = turn,
                    Overlap = Math.Max(0, Math.Min(end, turn.EndSec) - Math.Max(start, turn.StartSec)),
                    Distance = Math.Abs(((start + end) / 2) - ((turn.StartSec + turn.EndSec) / 2)),
                })
                .OrderByDescending(x => x.Overlap)
                .ThenBy(x => x.Distance)
                .FirstOrDefault();

            if (best is null)
            {
                continue;
            }

            var displayNumber = speakerMap[best.Turn.SpeakerId] + 1;
            segment.SpeakerId = $"speaker-{displayNumber}";
            segment.SpeakerName = $"Speaker {displayNumber}";
        }
    }

    internal static IReadOnlyList<TranscriptSegment> SplitAndAssignSpeakers(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<DiarSegment> diarSegments)
    {
        var speakerMap = BuildContiguousSpeakerMap(diarSegments);
        var result = new List<TranscriptSegment>();
        foreach (var segment in transcript)
        {
            var overlaps = diarSegments
                .Select(turn => new
                {
                    Turn = turn,
                    Start = Math.Max(segment.Start.TotalSeconds, turn.StartSec),
                    End = Math.Min(segment.End.TotalSeconds, turn.EndSec),
                })
                // Ignore tiny boundary intersections caused by timestamp rounding;
                // otherwise a 30 ms overlap can create a one-character speaker turn.
                .Where(x => x.End - x.Start >= 0.15)
                .OrderBy(x => x.Start)
                .ToList();

            if (overlaps.Count <= 1)
            {
                AssignSpeakers([segment], diarSegments);
                result.Add(segment);
                continue;
            }

            var text = segment.Text.Trim();
            var totalOverlap = overlaps.Sum(x => x.End - x.Start);
            var textOffset = 0;
            for (var i = 0; i < overlaps.Count; i++)
            {
                var overlap = overlaps[i];
                var remaining = text.Length - textOffset;
                var take = i == overlaps.Count - 1
                    ? remaining
                    : Math.Clamp(
                        (int)Math.Round(text.Length * ((overlap.End - overlap.Start) / totalOverlap)),
                        1,
                        Math.Max(1, remaining - (overlaps.Count - i - 1)));
                var pieceText = remaining > 0
                    ? text.Substring(textOffset, Math.Min(take, remaining)).Trim()
                    : string.Empty;
                textOffset += Math.Min(take, remaining);

                if (string.IsNullOrWhiteSpace(pieceText))
                {
                    continue;
                }

                var displayNumber = speakerMap[overlap.Turn.SpeakerId] + 1;
                result.Add(new TranscriptSegment
                {
                    SpeakerId = $"speaker-{displayNumber}",
                    SpeakerName = $"Speaker {displayNumber}",
                    SourceId = segment.SourceId,
                    SourceKind = segment.SourceKind,
                    Start = TimeSpan.FromSeconds(overlap.Start),
                    End = TimeSpan.FromSeconds(overlap.End),
                    Text = pieceText,
                    Confidence = segment.Confidence,
                    IsProvisional = segment.IsProvisional,
                });
            }
        }

        return result;
    }

    internal static IReadOnlyDictionary<int, int> BuildContiguousSpeakerMap(
        IReadOnlyList<DiarSegment> diarSegments)
    {
        return diarSegments
            .OrderBy(x => x.StartSec)
            .Select(x => x.SpeakerId)
            .Distinct()
            .Select((rawId, index) => (rawId, index))
            .ToDictionary(x => x.rawId, x => x.index);
    }

    private bool TryBuildWhisperRequest(string wavPath, out string executable, out string arguments, out string error)
    {
        executable = string.Empty;
        arguments = string.Empty;
        error = string.Empty;

        var encoder = ResolvePath(_options.WhisperEncoder);
        var decoder = ResolvePath(_options.WhisperDecoder);
        var tokens = ResolvePath(_options.WhisperTokens);

        var anyConfigured = !string.IsNullOrWhiteSpace(_options.WhisperEncoder)
            || !string.IsNullOrWhiteSpace(_options.WhisperDecoder)
            || !string.IsNullOrWhiteSpace(_options.WhisperTokens)
            || !string.IsNullOrWhiteSpace(_options.WhisperOfflineExecutable);

        // No Whisper config at all: silently fall back to paraformer.
        if (!anyConfigured)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(encoder) || !File.Exists(encoder))
        {
            error = "Whisper encoder model is configured but the file does not exist. Open Settings > Whisper to fix the path, or republish the app so the bundled Whisper large-v3 model is copied beside the executable.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(decoder) || !File.Exists(decoder))
        {
            error = "Whisper decoder model is configured but the file does not exist. Open Settings > Whisper to fix the path, or republish the app so the bundled Whisper large-v3 model is copied beside the executable.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tokens) || !File.Exists(tokens))
        {
            error = "Whisper tokens file is configured but the file does not exist. Open Settings > Whisper to fix the path, or republish the app so the bundled Whisper large-v3 model is copied beside the executable.";
            return false;
        }

        // Prefer a dedicated sherpa-onnx-offline.exe when the user configured one,
        // otherwise fall back to the bundled sherpa-onnx.exe which also supports
        // --whisper-* in newer builds. The dedicated exe gives a more stable
        // contract (e.g. it ignores unrelated streaming-related flags).
        var offlineExe = ResolvePath(_options.WhisperOfflineExecutable)
            ?? ResolvePath(_options.OfflineRecognizerExecutable);

        if (string.IsNullOrWhiteSpace(offlineExe) || !File.Exists(offlineExe))
        {
            error = "Whisper model files are present, but no working sherpa-onnx offline recognizer executable is configured. Rebuild or republish the app so models/sherpa-onnx is copied beside the executable.";
            return false;
        }

        executable = offlineExe;
        arguments = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "--whisper-encoder=\"{0}\" --whisper-decoder=\"{1}\" --tokens=\"{2}\" --num-threads=2 \"{3}\"",
            encoder,
            decoder,
            tokens,
            wavPath);
        return true;
    }

    /// <summary>
    /// Optional manifest-based request. Driven by the Models\catalog.json entries
    /// and the installed model files under models/{id}/. When the manifest id is
    /// recognized and all files are present, this produces the right CLI args
    /// for that family (Whisper / SenseVoice / Paraformer / Qwen3-ASR).
    /// </summary>
    public bool TryBuildManifestRequest(string? activeModelId, string wavPath,
        out string executable, out string arguments, out string error)
    {
        executable = string.Empty;
        arguments = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(activeModelId))
        {
            return false;
        }

        var catalogPath = Path.Combine(_baseDirectory, "Models", "catalog.json");
        if (!File.Exists(catalogPath))
        {
            // Catalog is optional; if missing, fall back to legacy Whisper path.
            return false;
        }

        try
        {
            var json = File.ReadAllText(catalogPath);
            var file = System.Text.Json.JsonSerializer.Deserialize<MeetingTransfer.Core.Models.ModelCatalogFile>(json);
            var model = file?.Models.FirstOrDefault(m =>
                string.Equals(m.Id, activeModelId, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                return false;
            }

            if (string.Equals(model.ExecutionMode, "online", StringComparison.OrdinalIgnoreCase))
            {
                // Online models are used by the realtime path, not by TranscribeFileAsync.
                error = $"Model '{model.Id}' is for real-time recording, not for file import.";
                return false;
            }

            var modelDir = Path.Combine(_baseDirectory, "models", Sanitize(model.Id));
            foreach (var f in model.Files)
            {
                var p = Path.Combine(modelDir, f.Name);
                if (!File.Exists(p))
                {
                    error = $"Model '{model.Id}' is not fully installed. Reinstall it from Settings.";
                    return false;
                }
            }

            var exePath = Path.IsPathRooted(model.Executable)
                ? model.Executable
                : Path.Combine(_baseDirectory, model.Executable);
            if (!File.Exists(exePath))
            {
                error = $"Model '{model.Id}' needs the sherpa-onnx runtime at '{model.Executable}', which is missing. Republish the app.";
                return false;
            }

            executable = exePath;
            arguments = BuildManifestArguments(model, modelDir, wavPath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to build manifest request: {ex.Message}";
            return false;
        }
    }

    private bool ShouldPreferWhisperCpp()
    {
        if (string.IsNullOrWhiteSpace(_options.WhisperCppExecutable) &&
            string.IsNullOrWhiteSpace(_options.WhisperCppModel) &&
            string.IsNullOrWhiteSpace(_options.WhisperCppArgumentsTemplate))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(_options.ActiveModelId) ||
            _options.ActiveModelId.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryBuildWhisperCppRequest(string wavPath, out string executable, out string arguments, out string error)
    {
        executable = string.Empty;
        arguments = string.Empty;
        error = string.Empty;

        var anyConfigured = !string.IsNullOrWhiteSpace(_options.WhisperCppExecutable)
            || !string.IsNullOrWhiteSpace(_options.WhisperCppModel)
            || !string.IsNullOrWhiteSpace(_options.WhisperCppArgumentsTemplate);
        if (!anyConfigured)
        {
            return false;
        }

        var modelPathFromCatalog = TryResolveActiveWhisperCppModel(
            out var catalogModelPath,
            out var catalogTemplate,
            out var languageOverride,
            out var catalogError);
        if (!string.IsNullOrEmpty(catalogError))
        {
            error = catalogError;
            return false;
        }

        var whisperCppExe = ResolvePath(_options.WhisperCppExecutable);
        if (string.IsNullOrWhiteSpace(whisperCppExe) || !File.Exists(whisperCppExe))
        {
            error = "whisper.cpp executable is configured but the file does not exist. Rebuild or republish the app so models/whisper-cpp-vulkan is copied beside the executable.";
            return false;
        }

        var whisperCppModel = modelPathFromCatalog ? catalogModelPath : ResolvePath(_options.WhisperCppModel);
        if (string.IsNullOrWhiteSpace(whisperCppModel) || !File.Exists(whisperCppModel))
        {
            error = "whisper.cpp model is configured but the file does not exist. Download ggml-large-v3-turbo.bin into models/whisper-cpp-vulkan/models, or fix the path in models.json.";
            return false;
        }

        var configuredTemplate = modelPathFromCatalog ? catalogTemplate : _options.WhisperCppArgumentsTemplate;
        var template = string.IsNullOrWhiteSpace(configuredTemplate)
            ? "-m \"{Model}\" -f \"{InputWav}\" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1"
            : configuredTemplate;
        arguments = template
            .Replace("{Model}", whisperCppModel, StringComparison.OrdinalIgnoreCase)
            .Replace("{InputWav}", wavPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{Language}", languageOverride ?? ResolveWhisperCppLanguage(_options.WhisperCppLanguage), StringComparison.OrdinalIgnoreCase);

        // Force the flags we need for fine-grained progress + structured output:
        //   -oj + -of <path>   -> write JSON with timestamps next to the input wav
        //   -pp                -> print progress = X% on stderr (consumed by RunProcessAsyncCore)
        //   -ml <N> -sow       -> whisper-cli pre-splits by sentence length / word boundary
        // We strip any user-supplied -nt / -np / -oj / -of / -ml / -sow / -pp first to
        // avoid duplicate or contradicting flags from a stale models.json.
        var jsonOutPath = wavPath + ".whisper";
        arguments = StripFlag(arguments, "nt") + " -pp";
        arguments = StripFlag(arguments, "np");        // we need the progress lines now
        arguments = StripFlag(arguments, "oj");
        arguments = StripFlag(arguments, "ojf");
        arguments = StripFlagWithValue(arguments, "of");
        arguments = StripFlagWithValue(arguments, "ml");
        if (!arguments.Contains("-sow", StringComparison.Ordinal))
        {
            arguments += _options.WhisperSplitOnWord ? " -sow" : string.Empty;
        }
        arguments += $" -oj -of \"{jsonOutPath}\" -ml {_options.WhisperSegmentMaxLen}";

        executable = whisperCppExe;
        return true;
    }

    private static string StripFlag(string args, string flag)
    {
        // Remove a space-separated flag (e.g. "-nt" or "-np") wherever it appears.
        var pattern = new System.Text.RegularExpressions.Regex(@"(^|\s)-" + System.Text.RegularExpressions.Regex.Escape(flag) + @"(\s|$)");
        return pattern.Replace(args, " ");
    }

    private static string StripFlagWithValue(string args, string flag)
    {
        // Remove "-flag value" pair (whitespace-separated). This is intentionally
        // simple — it doesn't handle quoted values, but whisper-cli flags we care
        // about (-of, -ml) take simple unquoted args.
        var pattern = new System.Text.RegularExpressions.Regex(@"(^|\s)-" + System.Text.RegularExpressions.Regex.Escape(flag) + @"\s+\S+");
        return pattern.Replace(args, " ");
    }

    private bool TryResolveActiveWhisperCppModel(out string modelPath, out string argumentsTemplate, out string? languageOverride, out string error)
    {
        modelPath = string.Empty;
        argumentsTemplate = string.Empty;
        languageOverride = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(_options.ActiveModelId) ||
            !_options.ActiveModelId.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var catalog = new MeetingTransfer.Core.Models.ModelCatalog(_baseDirectory);
        var model = catalog.FindById(_options.ActiveModelId);
        if (model is null)
        {
            error = $"Whisper model '{_options.ActiveModelId}' is not in the model catalog.";
            return false;
        }

        if (model.Files.Count == 0)
        {
            error = $"Whisper model '{model.Id}' has no downloadable model file.";
            return false;
        }

        var file = model.Files[0];
        modelPath = catalog.GetInstalledFilePath(model, file);
        if (!File.Exists(modelPath))
        {
            error = $"Whisper model '{model.Id}' is not installed. Install it from Settings.";
            return false;
        }

        argumentsTemplate = model.ArgumentsTemplate;
        if (model.Languages.Count == 1 &&
            string.Equals(model.Languages[0], "en", StringComparison.OrdinalIgnoreCase))
        {
            languageOverride = "en";
        }
        return true;
    }

    private static string ResolveWhisperCppLanguage(string? language)
    {
        return (language ?? "bilingual").Trim().ToLowerInvariant() switch
        {
            "en" or "english" => "en",
            "zh" or "chinese" => "zh",
            // whisper.cpp accepts a single language token. For Chinese-English
            // code-switched meetings, forcing zh preserves embedded English
            // better than auto-detect on the bundled bilingual test sample.
            "bilingual" or "zh-en" or "zh_en" => "zh",
            "auto" => "auto",
            _ => "zh",
        };
    }

    private async Task<IReadOnlyList<TranscriptSegment>> RunWhisperCppAsync(
        string executable,
        string arguments,
        string wavPath,
        string sourceId,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var duration = TryProbeAudioDuration(wavPath, out var probedDuration)
            ? probedDuration
            : TimeSpan.Zero;

        // Parse whisper.cpp's `progress = X%` stderr callbacks into progress reports.
        // Each window the model processes emits a percent that can exceed 100 when the
        // chunked whisper decoder rolls over, so we clamp to [0, 100].
        var lastReportedPercent = -1.0;
        var lastSegmentCount = 0;
        // whisper-cli writes <outPath>.json (it appends .json even when our -of value
        // already ends in .whisper, so the actual file ends up as <wavPath>.whisper.json).
        var finalJsonPath = wavPath + ".whisper.json";

        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.Transcribing, 0,
            "Starting whisper.cpp transcription..."));

        var output = await RunProcessAsyncWithStderr(executable, arguments, line =>
        {
            // whisper.cpp emits lines like:
            //   whisper_print_progress_callback: progress =  38%
            // We only forward when the value actually changes (avoid 60+ identical UI updates).
            var match = System.Text.RegularExpressions.Regex.Match(
                line, @"progress\s*=\s*(?<p>\d+)\s*%",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return;
            }
            var rawPercent = double.Parse(match.Groups["p"].Value, CultureInfo.InvariantCulture);
            var percent = Math.Clamp(rawPercent, 0d, 100d);
            if (Math.Abs(percent - lastReportedPercent) < 0.5)
            {
                return;
            }
            lastReportedPercent = percent;

            // If the model already wrote the JSON we can count its segments and
            // surface them to the UI; otherwise just report the percentage.
            var partialSegments = TryReadWhisperSegments(finalJsonPath);
            if (partialSegments is not null && partialSegments.Count != lastSegmentCount)
            {
                lastSegmentCount = partialSegments.Count;
                progress?.Report(TranscriptionProgress.ForStage(
                    TranscriptionStage.Transcribing,
                    percent,
                    $"whisper.cpp decoding... {partialSegments.Count} segment(s) so far"));
            }
            else
            {
                progress?.Report(TranscriptionProgress.ForStage(
                    TranscriptionStage.Transcribing, percent, null));
            }
        }, cancellationToken).ConfigureAwait(false);

        // After process exit, the final JSON is at finalJsonPath (whisper-cli -of <path>
        // adds the .json suffix). Prefer structured parsing over free-text extraction.
        var segments = TryReadWhisperSegments(finalJsonPath);
        if (segments is null || segments.Count == 0)
        {
            var text = ExtractWhisperCppTranscriptText(output);
            if (string.IsNullOrWhiteSpace(text))
            {
                progress?.Report(TranscriptionProgress.Indeterminate(TranscriptionStage.AsrComplete,
                    "whisper.cpp returned no transcript"));
                return [];
            }
            segments =
            [
                new WhisperRawSegment(0, (long)duration.TotalMilliseconds, text),
            ];
        }

        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.PostProcessing, 99,
            "Splitting into sentences..."));

        // Re-segment by sentence punctuation and gap-aware grouping so single-speaker
        // audio yields one segment per actual sentence rather than the raw chunks
        // emitted by whisper-cli.
        var sentences = SplitIntoSentences(segments, maxLen: _options.WhisperSegmentMaxLen,
            splitOnWord: _options.WhisperSplitOnWord,
            maxSegmentSeconds: _options.WhisperMaxSegmentSeconds);

        // Allocate one speaker id per call. Without this, every sentence in the
        // SplitIntoSentences output would get a fresh id, which then propagates
        // through EnsureSpeaker and produces a Speaker-list entry per sentence
        // (visible as "8 × Speaker 1" in the right-hand panel). We don't have
        // speaker diarisation in this build, so all segments belong to the same
        // speaker by definition.
        var sharedSpeakerId = NextSpeakerId();
        var result = sentences.Select(s => new TranscriptSegment
        {
            SpeakerId = sharedSpeakerId,
            SpeakerName = "Speaker 1",
            SourceId = sourceId,
            SourceKind = AudioSourceKind.ImportedFile,
            Start = TimeSpan.FromMilliseconds(s.StartMs),
            End = TimeSpan.FromMilliseconds(s.EndMs),
            Text = s.Text,
            IsProvisional = false,
        }).ToList();

        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.AsrComplete, 100,
            $"{result.Count} segment(s)"));

        // Cleanup: whisper-cli writes .json next to the input wav. Remove it once we've
        // successfully extracted the data so we don't leak 1-2 MB files into recordings/.
        TryDelete(finalJsonPath);

        return result;
    }

    private static IReadOnlyList<WhisperRawSegment>? TryReadWhisperSegments(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("transcription", out var transcription))
            {
                return null;
            }
            var result = new List<WhisperRawSegment>(transcription.GetArrayLength());
            foreach (var seg in transcription.EnumerateArray())
            {
                var offsets = seg.GetProperty("offsets");
                var from = offsets.GetProperty("from").GetInt64();
                var to = offsets.GetProperty("to").GetInt64();
                var text = seg.TryGetProperty("text", out var textEl)
                    ? textEl.GetString() ?? string.Empty
                    : string.Empty;
                result.Add(new WhisperRawSegment(from, to, text));
            }
            return result;
        }
        catch
        {
            // The file may be partially written by whisper-cli when we peek during streaming.
            return null;
        }
    }

    private readonly record struct WhisperRawSegment(long StartMs, long EndMs, string Text);

    /// <summary>
    /// Re-segment a flat list of whisper-cli JSON chunks into per-sentence segments.
    /// Three rules, in order:
    ///   1. Force a new segment when the raw text contains a sentence-ending punctuation
    ///      mark ('.', '?', '!', '。', '！', '？', '…'). This is the dominant signal
    ///      for single-speaker streams.
    ///   2. Force a segment when the gap to the next chunk is at least 700 ms — a
    ///      natural silence between sentences.
    ///   3. Force a segment when the running text length exceeds
    ///      <paramref name="maxLen"/> characters, even mid-word. This keeps UI cards
    ///      from becoming unreadable when whisper-cli emits very long strings.
    /// Timestamps are taken from the first and last raw chunk that contributed text
    /// to each new segment.
    /// </summary>
    private static IReadOnlyList<WhisperRawSegment> SplitIntoSentences(
        IReadOnlyList<WhisperRawSegment> raw,
        int maxLen,
        bool splitOnWord,
        double maxSegmentSeconds)
    {
        var result = new List<WhisperRawSegment>();
        if (raw.Count == 0)
        {
            return result;
        }

        var tokens = raw.Select(r =>
        {
            var text = (r.Text ?? string.Empty).Trim();
            var endsSentence = EndsWithSentencePunctuation(text);
            return new SentenceToken(r.StartMs, r.EndMs, text, endsSentence);
        }).ToList();

        long segStartMs = tokens[0].StartMs;
        var segText = new System.Text.StringBuilder();
        var segHadContent = false;

        void Flush(int nextTokenIndex)
        {
            var text = segText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                segText.Clear();
                segHadContent = false;
                return;
            }
            long segEndMs = segHadContent
                ? (nextTokenIndex > 0 ? tokens[nextTokenIndex - 1].EndMs : segStartMs)
                : segStartMs;
            result.Add(new WhisperRawSegment(segStartMs, segEndMs, text));
            segText.Clear();
            segHadContent = false;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (string.IsNullOrEmpty(t.Text))
            {
                if (segHadContent && i + 1 < tokens.Count)
                {
                    var next = tokens[i + 1];
                    var gapMs = next.StartMs - t.EndMs;
                    if (gapMs >= 700)
                    {
                        Flush(i + 1);
                        segStartMs = next.StartMs;
                        continue;
                    }
                }
                continue;
            }

            var wouldExceed = segText.Length + 1 + t.Text.Length > maxLen;
            var prevGapMs = i > 0 ? t.StartMs - tokens[i - 1].EndMs : 0;
            var startsNewSentence = (i > 0 && tokens[i - 1].EndsSentence) || prevGapMs >= 700;

            if (segHadContent && (startsNewSentence || wouldExceed))
            {
                Flush(i);
                segStartMs = t.StartMs;
            }
            else if (!segHadContent)
            {
                segStartMs = t.StartMs;
            }

            var appendText = t.Text;
            if (splitOnWord && wouldExceed && segText.Length > 0)
            {
                var lastSpace = appendText.LastIndexOf(' ');
                if (lastSpace > 2)
                {
                    appendText = appendText[..lastSpace];
                }
            }

            if (segText.Length > 0 && !char.IsPunctuation(appendText[0]))
            {
                segText.Append(' ');
            }
            segText.Append(appendText);
            segHadContent = true;

            if (t.EndsSentence)
            {
                Flush(i + 1);
            }
        }

        Flush(tokens.Count);

        // maxSegmentSeconds is currently only used as an informational hint here;
        // we leave it in the signature so the call site is forward-compatible with
        // a future "split long segments at fixed windows" mode.
        _ = maxSegmentSeconds;
        return result;
    }

    private static bool EndsWithSentencePunctuation(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        var i = text.Length - 1;
        while (i >= 0 && (text[i] == '"' || text[i] == '\'' || text[i] == ')' || text[i] == ']' || text[i] == ' '))
        {
            i--;
        }
        if (i < 0)
        {
            return false;
        }
        var c = text[i];
        return c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？' || c == '…';
    }

    private readonly record struct SentenceToken(long StartMs, long EndMs, string Text, bool EndsSentence);

    private static string BuildManifestArguments(MeetingTransfer.Core.Models.ModelDescriptor model, string modelDir, string wavPath)
    {
        var args = model.ArgumentsTemplate;
        foreach (var f in model.Files)
        {
            args = ReplaceManifestFilePlaceholders(args, f.Name, Path.Combine(modelDir, f.Name));
        }
        args = args.Replace("{InputWav}", wavPath, StringComparison.OrdinalIgnoreCase);
        return args;
    }

    private static string ReplaceManifestFilePlaceholders(string args, string fileName, string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        args = args.Replace("{" + stem + "}", filePath, StringComparison.OrdinalIgnoreCase);

        foreach (var alias in GetManifestPlaceholderAliases(fileName))
        {
            args = args.Replace("{" + alias + "}", filePath, StringComparison.OrdinalIgnoreCase);
        }

        return args;
    }

    private static IEnumerable<string> GetManifestPlaceholderAliases(string fileName)
    {
        var normalized = fileName
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        if (normalized.Contains("convfrontend", StringComparison.Ordinal))
        {
            yield return "ConvFrontend";
        }

        if (normalized.Contains("silerovad", StringComparison.Ordinal))
        {
            yield return "SileroVadModel";
        }

        if (normalized.Contains("encoder", StringComparison.Ordinal))
        {
            yield return "Encoder";
        }

        if (normalized.Contains("decoder", StringComparison.Ordinal))
        {
            yield return "Decoder";
        }

        if (normalized.Contains("tokens", StringComparison.Ordinal))
        {
            yield return "Tokens";
        }

        if (string.Equals(fileName, "model.onnx", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("modelonnx", StringComparison.Ordinal))
        {
            yield return "Model";
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var s = new string(chars);
        return s.Length > 80 ? s[..80] : s;
    }

    public Task<IReadOnlyList<TranscriptSegment>> FinalizeSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TranscriptSegment>>([]);

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    // Generic model runner used by the manifest-selected model path. Mirrors the
    // behavior of the legacy Whisper branch: short files go through a single call,
    // long files are split into 30 s chunks (sherpa-onnx 1.13.4's 30 s wrapper cap
    // also applies to Qwen3-ASR / Paraformer offline, so chunking is the safe
    // default for any offline model).
    private async Task<IReadOnlyList<TranscriptSegment>> RunModelAsync(
        string executable,
        string argumentsTemplate,
        string wavPath,
        string sourceId,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var duration = TryProbeAudioDuration(wavPath, out var probedDuration)
            ? probedDuration
            : TimeSpan.FromSeconds(30);

        const int chunkSeconds = 30;
        if (duration > TimeSpan.FromSeconds(chunkSeconds + 1))
        {
            return await ChunkedWhisperTranscribeAsync(
                executable,
                argumentsTemplate,
                wavPath,
                sourceId,
                chunkSeconds,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.Transcribing, 50,
            "Running model..."));
        var output = await RunProcessAsync(executable, argumentsTemplate, cancellationToken)
            .ConfigureAwait(false);
        var text = ExtractTranscriptText(output);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        return
        [
            new TranscriptSegment
            {
                SpeakerId = NextSpeakerId(),
                SpeakerName = "Speaker 1",
                SourceId = sourceId,
                SourceKind = AudioSourceKind.ImportedFile,
                Start = TimeSpan.Zero,
                End = duration,
                Text = text,
                IsProvisional = false
            }
        ];
    }

    // sherpa-onnx 1.13.4 caps each Whisper call at 30 s. Split the input wav into
    // overlapping 30 s chunks, run each chunk separately, and stitch the segments
    // back together. Overlap is 5 s so a sentence straddling a chunk boundary is
    // captured by at least one chunk in full.
    private async Task<IReadOnlyList<TranscriptSegment>> ChunkedWhisperTranscribeAsync(
        string whisperExecutable,
        string whisperArgumentsTemplate,
        string wavPath,
        string sourceId,
        int chunkSeconds,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int overlapSeconds = 5;
        var duration = TryProbeAudioDuration(wavPath, out var probed)
            ? probed
            : throw new InvalidOperationException(
                $"ffprobe could not determine the duration of '{wavPath}'. " +
                "Chunked Whisper transcription requires ffmpeg/ffprobe to be installed next to the executable.");

        // Collect raw per-chunk segments first, then re-segment by sentence punctuation
        // at the end. This matches RunWhisperCppAsync's behaviour so the UI sees
        // natural sentence boundaries, not the artificial 30 s chunk grid.
        var rawSegments = new List<(long StartMs, long EndMs, string Text)>();
        var speakerId = NextSpeakerId();
        var stepSeconds = chunkSeconds - overlapSeconds;
        var totalSteps = Math.Max(1, (int)Math.Ceiling((duration.TotalSeconds - overlapSeconds) / stepSeconds));
        var tempFiles = new List<string>();
        var stepIndex = 0;
        try
        {
            for (var offset = 0.0; offset < duration.TotalSeconds; offset += stepSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(TranscriptionProgress.ForStage(
                    TranscriptionStage.Transcribing,
                    Math.Min(99.0, 50.0 + (stepIndex / (double)totalSteps) * 49.0),
                    $"Chunk {stepIndex + 1}/{totalSteps}..."));

                var chunkPath = Path.Combine(
                    Path.GetTempPath(),
                    $"meeting-transfer-whisper-chunk-{Guid.NewGuid():N}.wav");
                tempFiles.Add(chunkPath);

                var actualChunkSeconds = Math.Min(chunkSeconds, duration.TotalSeconds - offset);
                if (actualChunkSeconds <= 0)
                {
                    TryDelete(chunkPath);
                    tempFiles.Remove(chunkPath);
                    break;
                }

                await ExtractWavChunkAsync(wavPath, chunkPath, TimeSpan.FromSeconds(offset), TimeSpan.FromSeconds(actualChunkSeconds), cancellationToken)
                    .ConfigureAwait(false);

                // Build per-chunk arguments: replace the original wav path with the
                // chunk path, then strip conflicting flags and force JSON output + progress.
                var chunkJsonPath = chunkPath + ".json";
                var arguments = whisperArgumentsTemplate.Replace(
                    $"\"{wavPath}\"",
                    $"\"{chunkPath}\"",
                    StringComparison.OrdinalIgnoreCase);
                if (string.Equals(arguments, whisperArgumentsTemplate, StringComparison.OrdinalIgnoreCase))
                {
                    arguments = arguments.TrimEnd() + " " + $"\"{chunkPath}\"";
                }
                arguments = StripFlag(arguments, "nt");
                arguments = StripFlag(arguments, "np");
                arguments = StripFlag(arguments, "oj");
                arguments = StripFlag(arguments, "ojf");
                arguments = StripFlagWithValue(arguments, "of");
                arguments = StripFlagWithValue(arguments, "ml");
                if (!arguments.Contains("-sow", StringComparison.Ordinal) && _options.WhisperSplitOnWord)
                {
                    arguments += " -sow";
                }
                arguments += $" -pp -oj -of \"{chunkJsonPath}\" -ml {_options.WhisperSegmentMaxLen}";

                // Use the streaming reader so the user's progress bar advances for every
                // 30 s chunk that whisper-cli finishes decoding. Errors still surface
                // as exceptions via RunProcessAsyncCore.
                var output = await RunProcessAsyncWithStderr(whisperExecutable, arguments, line =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        line, @"progress\s*=\s*(?<p>\d+)\s*%",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success)
                    {
                        return;
                    }
                    var innerPercent = Math.Clamp(double.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture), 0d, 100d);
                    // Map inner-chunk progress [0, 100] onto this chunk's slice of the
                    // overall [50, 99] envelope so the UI bar moves smoothly.
                    var overallPercent = Math.Min(99.0,
                        50.0 + (stepIndex + innerPercent / 100.0) / totalSteps * 49.0);
                    progress?.Report(TranscriptionProgress.ForStage(
                        TranscriptionStage.Transcribing, overallPercent,
                        $"Chunk {stepIndex + 1}/{totalSteps} ({innerPercent:0}%)"));
                }, cancellationToken).ConfigureAwait(false);

                // Parse JSON instead of stdout so timestamps are accurate to the
                // sentence, not the chunk.
                var chunkSegments = TryReadWhisperSegments(chunkJsonPath);
                if (chunkSegments is null || chunkSegments.Count == 0)
                {
                    // Fallback to stdout text — old path, no per-sentence timing.
                    var text = ExtractTranscriptText(output);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        chunkSegments =
                        [
                            new WhisperRawSegment(0, (long)(actualChunkSeconds * 1000), text),
                        ];
                    }
                }

                if (chunkSegments is not null)
                {
                    var chunkOffsetMs = (long)(offset * 1000);
                    foreach (var cs in chunkSegments)
                    {
                        rawSegments.Add((
                            chunkOffsetMs + cs.StartMs,
                            chunkOffsetMs + cs.EndMs,
                            cs.Text));
                    }
                }

                TryDelete(chunkJsonPath);
                stepIndex++;
            }
        }
        finally
        {
            foreach (var tempFile in tempFiles)
            {
                TryDelete(tempFile);
            }
        }

        // Sentence-level segmentation across chunk boundaries. The raw segments span
        // the full audio timeline (because we added chunkOffsetMs above), so
        // SplitIntoSentences has the context it needs to merge cross-chunk sentences.
        progress?.Report(TranscriptionProgress.ForStage(
            TranscriptionStage.PostProcessing, 99, "Splitting into sentences..."));

        var sentences = SplitIntoSentences(
            rawSegments.Select(r => new WhisperRawSegment(r.StartMs, r.EndMs, r.Text)).ToList(),
            maxLen: _options.WhisperSegmentMaxLen,
            splitOnWord: _options.WhisperSplitOnWord,
            maxSegmentSeconds: _options.WhisperMaxSegmentSeconds);

        var segments = sentences.Select(s => new TranscriptSegment
        {
            SpeakerId = speakerId,
            SpeakerName = "Speaker 1",
            SourceId = sourceId,
            SourceKind = AudioSourceKind.ImportedFile,
            Start = TimeSpan.FromMilliseconds(s.StartMs),
            End = TimeSpan.FromMilliseconds(s.EndMs),
            Text = s.Text,
            IsProvisional = false,
        }).ToList();

        progress?.Report(TranscriptionProgress.ForStage(TranscriptionStage.AsrComplete, 100,
            $"{segments.Count} segment(s)"));

        return segments;
    }

    private async Task ExtractWavChunkAsync(
        string inputPath,
        string outputPath,
        TimeSpan startOffset,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var ffmpeg = ResolveFfmpegPath();
        if (string.IsNullOrEmpty(ffmpeg))
        {
            throw new FileNotFoundException(
                "ffmpeg.exe was not found. Whisper chunked transcription needs ffmpeg to slice the audio; the binary is bundled under 'models/ffmpeg/bin/ffmpeg.exe'.");
        }

        var start = startOffset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var dur = duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-y", "-ss", start, "-i", inputPath, "-t", dur, "-vn", "-ac", "1",
            "-ar", "16000", "-sample_fmt", "s16", "-loglevel", "error", outputPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start ffmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var error = await errorTask.ConfigureAwait(false);
                throw new InvalidOperationException($"ffmpeg failed to slice audio: {error}");
            }
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            TryDelete(outputPath);
            throw;
        }
    }

    private bool TryProbeAudioDuration(string wavPath, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var ffprobe = ResolveFfprobePath();
        if (string.IsNullOrEmpty(ffprobe) || !File.Exists(wavPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{wavPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();
            var output = outputTask.GetAwaiter().GetResult().Trim();
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                duration = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        catch
        {
            // best-effort probing
        }
        return false;
    }

    private string? ResolveFfmpegPath()
    {
        var bundled = Path.Combine(_baseDirectory, "models", "ffmpeg", "bin", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    private string? ResolveFfprobePath()
    {
        var bundled = Path.Combine(_baseDirectory, "models", "ffmpeg", "bin", "ffprobe.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    private string NextSpeakerId()
    {
        _speakerCounter++;
        return $"speaker-{_speakerCounter}";
    }

    private SourceAudioBuffer GetBuffer(PcmAudioChunk chunk)
    {
        var key = $"{chunk.SourceKind}:{chunk.SourceId}";
        if (_buffers.TryGetValue(key, out var buffer))
        {
            return buffer;
        }

        buffer = new SourceAudioBuffer(chunk.SourceId, chunk.SourceKind, chunk.SampleRate, chunk.Channels);
        _buffers.Add(key, buffer);
        return buffer;
    }

    private string ApplyTemplate(string? template, string inputWav)
    {
        template ??= "\"{InputWav}\"";
        return template
            .Replace("{InputWav}", inputWav, StringComparison.OrdinalIgnoreCase)
            .Replace("{Tokens}", ResolvePath(_options.Tokens) ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{Encoder}", ResolvePath(_options.Encoder) ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{Decoder}", ResolvePath(_options.Decoder) ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{Joiner}", ResolvePath(_options.Joiner) ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{SileroVadModel}", ResolvePath(_options.SileroVadModel) ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(_baseDirectory, path);
    }

    private static async Task<string> RunProcessAsync(string executable, string arguments, CancellationToken cancellationToken)
    {
        var (stdout, _) = await RunProcessAsyncCore(executable, arguments, onStderrLine: null, cancellationToken)
            .ConfigureAwait(false);
        return stdout;
    }

    /// <summary>
    /// Like <see cref="RunProcessAsync(string,string,CancellationToken)"/> but
    /// invokes <paramref name="onStderrLine"/> for every complete line emitted
    /// on stderr. Used to surface whisper.cpp's `progress = X%` callback to the
    /// UI while the process is still running.
    /// </summary>
    private static async Task<string> RunProcessAsyncWithStderr(
        string executable,
        string arguments,
        Action<string> onStderrLine,
        CancellationToken cancellationToken)
    {
        var (stdout, _) = await RunProcessAsyncCore(executable, arguments, onStderrLine, cancellationToken)
            .ConfigureAwait(false);
        return stdout;
    }

    private static async Task<(string Stdout, string Stderr)> RunProcessAsyncCore(
        string executable,
        string arguments,
        Action<string>? onStderrLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {executable}.");

        var stderrBuilder = new System.Text.StringBuilder();

        // We hold stdoutReader/stderrReader so we can dispose them on cancellation,
        // which unblocks the underlying ReadXxxAsync calls. Without this the
        // WaitForExitAsync below would hang forever waiting for the process to
        // finish writing to a pipe we no longer care about.
        Task<string> stdoutTask;
        Task stderrTask;
        System.IO.StreamReader stdoutReader;
        System.IO.StreamReader stderrReader;
        try
        {
            stdoutReader = process.StandardOutput;
            stderrReader = process.StandardError;
            stdoutTask = stdoutReader.ReadToEndAsync(cancellationToken);
            stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await stderrReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    stderrBuilder.AppendLine(line);
                    if (onStderrLine is not null)
                    {
                        try
                        {
                            onStderrLine(line);
                        }
                        catch
                        {
                            // never let a UI-side progress callback kill the process reader
                        }
                    }
                }
            }, cancellationToken);
        }
        catch
        {
            // Process never started cleanly; kill whatever's running and bail.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string stdoutText;
        try
        {
            stdoutText = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancelled. Kill the spawned process and wait (without the token)
            // so we don't leak orphan processes when the UI thread disposes this engine.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best-effort; if Kill fails we still want to surface the cancellation
            }
            try
            {
                // Drain whatever the readers have buffered so they don't deadlock
                // against the killed process's stdout/stderr file descriptors.
                await stdoutTask.ConfigureAwait(false);
                await stderrTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored — we're already in the cancel path
            }
            try
            {
                // WaitForExit without a token so we actually return after Kill().
                process.WaitForExit(2000);
            }
            catch
            {
                // ignored
            }
            throw;
        }

        var stderrText = stderrBuilder.ToString();

        // sherpa-onnx cli is inconsistent about which stream carries results:
        //   * sherpa-onnx-vad-with-online-asr.exe  -> per-segment text on stdout, stats on stderr
        //   * sherpa-onnx.exe (offline)            -> init banner on stdout, JSON + text on stderr
        // Merge both streams so ExtractTranscriptText can pick the right lines regardless.
        var combined = string.Join(
            "\n",
            new[] { stdoutText, stderrText }.Where(s => !string.IsNullOrEmpty(s)));

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sherpa-onnx exited with {process.ExitCode}: {stderrText}");
        }

        return (combined, stderrText);
    }

    private static string ExtractTranscriptText(string output)
    {
        var trimmed = output.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "";
        }

        // 1. Prefer per-segment VAD results (longest meaningful segment wins).
        var vadSegments = VadSegmentRegex().Matches(trimmed);
        if (vadSegments.Count > 0)
        {
            var best = vadSegments
                .Select(m => m.Groups["text"].Value.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length >= 2)
                .OrderByDescending(t => t.Length)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(best))
            {
                return best;
            }
        }

        // 2. JSON { "text": "..." } lines (offline recognizer).
        var jsonCandidateLines = trimmed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var lineIndex = jsonCandidateLines.Length - 1; lineIndex >= 0; lineIndex--)
        {
            var line = jsonCandidateLines[lineIndex];
            var lineTrimmed = line.Trim();
            if (!lineTrimmed.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(lineTrimmed);
                if (document.RootElement.TryGetProperty("text", out var textElement))
                {
                    var t = textElement.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(t))
                    {
                        return t;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to plain-line parsing below.
            }
        }

        // 3. Generic "text:" / "result:" / "transcript:" lines.
        var match = TranscriptLineRegex().Match(trimmed);
        if (match.Success)
        {
            return match.Groups["text"].Value.Trim();
        }

        // 4. Some recognizers emit plain text without a prefix. Keep only lines
        // that are not known sherpa configuration/timing diagnostics. In
        // particular, a silence-only online run contains dozens of stderr lines
        // but no transcript; returning the whole blob here used to create a giant
        // fake segment every four seconds.
        return trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !IsSherpaDiagnosticLine(line))
            .OrderByDescending(line => line.Length)
            .FirstOrDefault() ?? "";
    }

    internal static bool IsSherpaDiagnosticLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var prefixes = new[]
        {
            "VadModelConfig(", "OnlineRecognizerConfig(", "OfflineRecognizerConfig(",
            "Creating recognizer", "Recognizer created", "Started", "Reading:",
            "num threads:", "Number of threads:", "decoding method:",
            "Elapsed seconds:", "Real time factor", "Duration :", "progress ",
            "Start to create recognizer",
        };
        return prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
               line.Contains("parse-options.cc:Read:", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsEffectivelySilentPcm16(byte[] pcm, double thresholdDb)
    {
        if (pcm.Length < 2)
        {
            return true;
        }

        double sumSquares = 0;
        var sampleCount = pcm.Length / 2;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = (short)(pcm[i] | (pcm[i + 1] << 8));
            var normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        var db = rms <= double.Epsilon ? double.NegativeInfinity : 20 * Math.Log10(rms);
        return db < thresholdDb;
    }

    private static string ExtractWhisperCppTranscriptText(string output)
    {
        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("ggml_", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("whisper_", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("system_info:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("main:", StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var match = WhisperCppTimestampLineRegex().Match(line);
                var text = match.Success ? match.Groups["text"].Value.Trim() : line;
                return WhisperCppTimingSuffixRegex().Replace(text, "").Trim();
            })
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return string.Join(" ", lines).Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temporary files are best-effort cleanup.
        }
    }

    [GeneratedRegex(@"(?im)^(?:text|result|transcript)\s*[:=]\s*(?<text>.+)$")]
    private static partial Regex TranscriptLineRegex();

    [GeneratedRegex(@"(?im)^vad\s+segment\([^)]*\)\s+results:\s*(?<text>.+)$")]
    private static partial Regex VadSegmentRegex();

    [GeneratedRegex(@"^\[[^\]]+\]\s*(?<text>.+)$")]
    private static partial Regex WhisperCppTimestampLineRegex();

    [GeneratedRegex(@"\s*whisper_print_timings:.*$")]
    private static partial Regex WhisperCppTimingSuffixRegex();

    private sealed class SourceAudioBuffer
    {
        private readonly MemoryStream _pcm = new();
        private readonly string _sourceId;
        private readonly AudioSourceKind _sourceKind;
        private readonly int _sampleRate;
        private readonly int _channels;
        private TimeSpan _start = TimeSpan.Zero;
        private TimeSpan _end = TimeSpan.Zero;
        private bool _hasAudio;

        public SourceAudioBuffer(string sourceId, AudioSourceKind sourceKind, int sampleRate, int channels)
        {
            _sourceId = sourceId;
            _sourceKind = sourceKind;
            _sampleRate = sampleRate;
            _channels = channels;
        }

        public TimeSpan Duration => _hasAudio ? _end - _start : TimeSpan.Zero;
        public TimeSpan FlushedStart { get; private set; }
        public TimeSpan FlushedEnd { get; private set; }

        public void Append(PcmAudioChunk chunk)
        {
            if (!_hasAudio)
            {
                _start = chunk.SessionOffset;
                _hasAudio = true;
            }

            _end = chunk.SessionOffset + PcmDuration(chunk.Pcm16.Length, chunk.SampleRate, chunk.Channels);
            _pcm.Write(chunk.Pcm16, 0, chunk.Pcm16.Length);
        }

        public string FlushToTempWav()
        {
            FlushedStart = _start;
            FlushedEnd = _end;
            var path = Path.Combine(
                Path.GetTempPath(),
                $"meeting-transfer-{_sourceKind}-{Sanitize(_sourceId)}-{Guid.NewGuid():N}.wav");

            File.WriteAllBytes(path, BuildWav(_pcm.ToArray(), _sampleRate, _channels));
            Reset();
            return path;
        }

        public bool IsEffectivelySilent(double thresholdDb)
            => IsEffectivelySilentPcm16(_pcm.ToArray(), thresholdDb);

        public void Discard()
            => Reset();

        private void Reset()
        {
            _pcm.SetLength(0);
            _hasAudio = false;
            _start = TimeSpan.Zero;
            _end = TimeSpan.Zero;
        }

        private static TimeSpan PcmDuration(int byteCount, int sampleRate, int channels)
            => TimeSpan.FromSeconds(byteCount / (double)(sampleRate * channels * 2));

        private static byte[] BuildWav(byte[] pcm, int sampleRate, int channels)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            var byteRate = sampleRate * channels * 2;
            var blockAlign = (short)(channels * 2);

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + pcm.Length);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(pcm.Length);
            writer.Write(pcm);
            return stream.ToArray();
        }
    }
}
