using System.Reflection;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.Tests;

public sealed class SherpaOnnxOutputParserTests
{
    [Fact]
    public void ExtractsVadSegmentResultInsteadOfRawLog()
    {
        const string output = """
            Started!
            vad segment(1:0.678-2.284) results: 昨天是 monday
            vad segment(2:3.814-5.932) results: 台灯 is 礼拜二
            num threads: 1
            Elapsed seconds: 1.860 s
            """;

        var text = ExtractTranscriptText(output);

        Assert.Equal("昨天是 monday", text);
        Assert.DoesNotContain("Elapsed seconds", text);
    }

    [Fact]
    public void ExtractsJsonTextFromOfflineRecognizerOutput()
    {
        const string output = """
            Number of threads: 1
            { "text": "hello world", "segment": 0, "is_final": false }
            """;

        var text = ExtractTranscriptText(output);

        Assert.Equal("hello world", text);
    }

    [Fact]
    public void SilenceOnlySherpaDiagnosticsReturnNoTranscript()
    {
        const string output = """
            D:\a\sherpa-onnx\csrc\parse-options.cc:Read:374 'sherpa-onnx-vad-with-online-asr.exe'
            VadModelConfig(silero_vad=SileroVadModelConfig(model="silero_vad.onnx"))
            OnlineRecognizerConfig(feat_config=FeatureExtractorConfig(sampling_rate=16000))
            Creating recognizer ...
            Recognizer created!
            Started
            Reading: silence.wav
            Started!
            num threads: 2
            decoding method: greedy_search
            Elapsed seconds: 0.036 s
            Real time factor (RTF): 0.036 / 4.000 = 0.009
            """;

        Assert.Equal("", ExtractTranscriptText(output));
    }

    [Fact]
    public void RealtimeSilenceGateDistinguishesSilenceFromSpeechLevelPcm()
    {
        var silence = new byte[16000 * 2];
        var speech = new byte[16000 * 2];
        for (var i = 0; i < speech.Length; i += 2)
        {
            const short sample = 2000;
            speech[i] = (byte)(sample & 0xff);
            speech[i + 1] = (byte)((sample >> 8) & 0xff);
        }

        Assert.True(SherpaOnnxSpeechEngine.IsEffectivelySilentPcm16(silence, -50));
        Assert.False(SherpaOnnxSpeechEngine.IsEffectivelySilentPcm16(speech, -50));
    }

    // Regression: sherpa-onnx.exe (offline) writes the init banner to stdout and the
    // recognized text + JSON to stderr. The previous parser only looked at stdout, so the
    // UI rendered "Start to create recognizer / Recognizer created in N s" as the transcript.
    // The fix merges stdout and stderr before parsing; this test exercises the merged input.
    [Fact]
    public void ExtractsJsonTextWhenInitBannerIsOnStdout()
    {
        const string stdout = "Start to create recognizer\nRecognizer created in 3.08824 s\n";
        const string stderr =
            "Number of threads: 2, Elapsed seconds: 1.2, Audio duration (s): 10, Real time factor (RTF) = 1.2/10 = 0.12\n" +
            "昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期\n" +
            "{ \"text\": \"昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期\", \"segment\": 0, \"is_final\": false }\n";

        var merged = string.Join("\n", stdout, stderr);

        var text = ExtractTranscriptText(merged);

        Assert.Equal("昨天是 monday tedis is 礼拜二 the day after tomorrow 是星期", text);
        Assert.DoesNotContain("Start to create recognizer", text);
        Assert.DoesNotContain("Recognizer created", text);
    }

    [Fact]
    public async Task MergesStdoutAndStderrInRunProcessAsync()
    {
        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "RunProcessAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var task = (Task<string>)method!.Invoke(null, [
            "cmd.exe",
            "/c echo STDOUT_LINE & echo STDERR_LINE 1>&2",
            CancellationToken.None
        ])!;
        var merged = await task;

        Assert.Contains("STDOUT_LINE", merged);
        Assert.Contains("STDERR_LINE", merged);
    }

    [Fact]
    public async Task RepeatedRealtimeSilenceWindowsDoNotLaunchRecognizer()
    {
        // The executable is intentionally missing. If the silence gate fails,
        // ProcessAudioAsync will try to launch it and the test will throw.
        await using var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
        {
            OnlineRecognizerExecutable = "definitely-missing.exe",
            RealtimeSilenceThresholdDb = -50,
        });
        var silence = new byte[16000 * 2 * 4];

        for (var i = 0; i < 6; i++)
        {
            var segments = await engine.ProcessAudioAsync(
                new MeetingTransfer.Core.Audio.PcmAudioChunk(
                    "silent-device",
                    MeetingTransfer.Core.Audio.AudioSourceKind.Microphone,
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromSeconds(i * 4),
                    16000,
                    1,
                    silence),
                CancellationToken.None);

            Assert.Empty(segments);
        }
    }

    [Fact]
    public void WhisperRequestIsNotBuiltWhenNoWhisperConfigIsPresent()
    {
        // When none of the Whisper* options are set, the engine should fall back
        // to the paraformer model — TryBuildWhisperRequest must return false with
        // no error so the caller transparently uses OfflineRecognizerExecutable.
        var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
        {
            OfflineRecognizerExecutable = "models/sherpa-onnx/bin/sherpa-onnx.exe",
        });

        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "TryBuildWhisperRequest",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var args = new object?[] { "input.wav", null, null, null };
        var built = (bool)method.Invoke(engine, args)!;
        var executable = (string)args[1]!;
        var arguments = (string)args[2]!;
        var error = (string)args[3]!;

        Assert.False(built);
        Assert.Equal(string.Empty, executable);
        Assert.Equal(string.Empty, arguments);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void WhisperRequestIsBuiltWhenAllModelFilesAreConfigured()
    {
        // Use a real bundled model file (encoder) plus a fake decoder/tokens
        // that exist on disk so the engine accepts the configuration. The
        // argument string should reference the dedicated offline exe and
        // include --whisper-encoder/--whisper-decoder/--tokens but NOT
        // --language (which sherpa-onnx 1.13.4 does not accept on offline).
        var bundledEncoder = Path.Combine(
            AppContext.BaseDirectory,
            "models", "sherpa-onnx", "models", "silero_vad.onnx");

        if (!File.Exists(bundledEncoder))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "mt-whisper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeDecoder = Path.Combine(tempDir, "decoder.onnx");
            var fakeTokens = Path.Combine(tempDir, "tokens.txt");
            File.WriteAllText(fakeDecoder, "fake");
            File.WriteAllText(fakeTokens, "fake");

            var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
            {
                OfflineRecognizerExecutable = "models/sherpa-onnx/bin/sherpa-onnx.exe",
                WhisperOfflineExecutable = "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
                WhisperEncoder = bundledEncoder,
                WhisperDecoder = fakeDecoder,
                WhisperTokens = fakeTokens,
            });

            var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
                "TryBuildWhisperRequest",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var args = new object?[] { "input.wav", null, null, null };
            var built = (bool)method.Invoke(engine, args)!;
            var executable = (string)args[1]!;
            var arguments = (string)args[2]!;
            var error = (string)args[3]!;

            Assert.True(built);
            Assert.Equal(string.Empty, error);
            Assert.EndsWith("sherpa-onnx-offline.exe", executable);
            Assert.Contains("--whisper-encoder=", arguments);
            Assert.Contains("--whisper-decoder=", arguments);
            Assert.Contains("--tokens=", arguments);
            Assert.Contains("--num-threads=2", arguments);
            Assert.Contains("input.wav", arguments);
            Assert.DoesNotContain("--language", arguments);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WhisperRequestReportsClearErrorWhenEncoderFileMissing()
    {
        var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
        {
            WhisperEncoder = "models/sherpa-onnx-whisper/missing-encoder.onnx",
            WhisperDecoder = "models/sherpa-onnx-whisper/missing-decoder.onnx",
            WhisperTokens = "models/sherpa-onnx-whisper/missing-tokens.txt",
            WhisperOfflineExecutable = "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
        });

        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "TryBuildWhisperRequest",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var args = new object?[] { "input.wav", null, null, null };
        var built = (bool)method.Invoke(engine, args)!;
        var error = (string)args[3]!;

        Assert.False(built);
        Assert.NotEmpty(error);
        Assert.Contains("encoder", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhisperCppRequestIsBuiltWhenRuntimeAndModelExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mt-whisper-cpp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var exe = Path.Combine(tempDir, "whisper-cli.exe");
            var model = Path.Combine(tempDir, "ggml-large-v3-turbo.bin");
            File.WriteAllText(exe, "fake");
            File.WriteAllText(model, "fake");

            var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
            {
                WhisperCppExecutable = exe,
                WhisperCppModel = model,
                WhisperCppArgumentsTemplate = "-m \"{Model}\" -f \"{InputWav}\" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np",
                WhisperCppLanguage = "bilingual",
            });

            var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
                "TryBuildWhisperCppRequest",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var args = new object?[] { "input.wav", null, null, null };
            var built = (bool)method.Invoke(engine, args)!;
            var executable = (string)args[1]!;
            var arguments = (string)args[2]!;
            var error = (string)args[3]!;

            Assert.True(built);
            Assert.Equal(string.Empty, error);
            Assert.Equal(exe, executable);
            Assert.Contains($"-m \"{model}\"", arguments);
            Assert.Contains("-f \"input.wav\"", arguments);
            Assert.Contains("-l zh", arguments);
            Assert.Contains("-dev 0", arguments);
            Assert.Contains("-nfa", arguments);
            Assert.Contains("-bs 1", arguments);
            Assert.Contains("-bo 1", arguments);
            Assert.DoesNotContain("{Model}", arguments);
            Assert.DoesNotContain("{InputWav}", arguments);
            Assert.DoesNotContain("{Language}", arguments);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WhisperCppRequestUsesSelectedEnglishLanguage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mt-whisper-cpp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var exe = Path.Combine(tempDir, "whisper-cli.exe");
            var model = Path.Combine(tempDir, "ggml-base.bin");
            File.WriteAllText(exe, "fake");
            File.WriteAllText(model, "fake");

            var engine = new SherpaOnnxSpeechEngine(new SherpaOnnxOptions
            {
                WhisperCppExecutable = exe,
                WhisperCppModel = model,
                WhisperCppArgumentsTemplate = "-m \"{Model}\" -f \"{InputWav}\" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np",
                WhisperCppLanguage = "en",
            });

            var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
                "TryBuildWhisperCppRequest",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var args = new object?[] { "input.wav", null, null, null };
            var built = (bool)method.Invoke(engine, args)!;
            var arguments = (string)args[2]!;

            Assert.True(built);
            Assert.Contains("-l en", arguments);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractsWhisperCppTextWithoutGpuLogs()
    {
        const string output = """
            ggml_vulkan: Found 2 Vulkan devices:
            ggml_vulkan: 0 = AMD Radeon RX 7600M XT
            [00:00:00.000 --> 00:00:02.000]  这是第一句
            [00:00:02.000 --> 00:00:04.000]  this is the second sentence whisper_print_timings: total time = 1234.56 ms
            whisper_print_timings: total time = 1234.56 ms
            """;

        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "ExtractWhisperCppTranscriptText",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var text = Assert.IsType<string>(method!.Invoke(null, [output]));

        Assert.Equal("这是第一句 this is the second sentence", text);
    }

    [Fact]
    public void ManifestArgumentsReplaceCommonAliasesForVersionedModelFiles()
    {
        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "BuildManifestArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var model = new MeetingTransfer.Core.Models.ModelDescriptor
        {
            Id = "whisper-large-v3-int8",
            Files =
            [
                new MeetingTransfer.Core.Models.ModelFileEntry { Name = "large-v3-encoder.int8.onnx" },
                new MeetingTransfer.Core.Models.ModelFileEntry { Name = "large-v3-decoder.int8.onnx" },
                new MeetingTransfer.Core.Models.ModelFileEntry { Name = "large-v3-tokens.txt" },
            ],
            ArgumentsTemplate = "--whisper-encoder=\"{Encoder}\" --whisper-decoder=\"{Decoder}\" --tokens=\"{Tokens}\" \"{InputWav}\""
        };

        var args = (string)method!.Invoke(null, [model, "C:/models/whisper-large-v3-int8", "C:/input.wav"])!;

        Assert.Contains("large-v3-encoder.int8.onnx", args);
        Assert.Contains("large-v3-decoder.int8.onnx", args);
        Assert.Contains("large-v3-tokens.txt", args);
        Assert.DoesNotContain("{Encoder}", args);
        Assert.DoesNotContain("{Decoder}", args);
        Assert.DoesNotContain("{Tokens}", args);
        Assert.DoesNotContain("{InputWav}", args);
    }

    private static string ExtractTranscriptText(string output)
    {
        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "ExtractTranscriptText",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [output]));
    }
}
