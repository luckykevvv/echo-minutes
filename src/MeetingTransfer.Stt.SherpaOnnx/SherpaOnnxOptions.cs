namespace MeetingTransfer.Stt.SherpaOnnx;

public sealed class SherpaOnnxOptions
{
    public string? OnlineRecognizerExecutable { get; set; }
    public string? OfflineRecognizerExecutable { get; set; }
    public string? OnlineArgumentsTemplate { get; set; }
    public string? OfflineArgumentsTemplate { get; set; }
    public string? Tokens { get; set; }
    public string? Encoder { get; set; }
    public string? Decoder { get; set; }
    public string? Joiner { get; set; }
    public string? SileroVadModel { get; set; }

    // Whisper large-v3 (used for the offline / file-import path when present).
    // When all three of these resolve to existing files, the file-import path
    // uses sherpa-onnx.exe with --whisper-encoder/--whisper-decoder instead of
    // the paraformer model, which gives much higher accuracy on Chinese and
    // multilingual audio.
    public string? WhisperEncoder { get; set; }
    public string? WhisperDecoder { get; set; }
    public string? WhisperTokens { get; set; }
    public string? WhisperOfflineExecutable { get; set; }
    public string? WhisperArgumentsTemplate { get; set; }

    // whisper.cpp Vulkan runtime. When present, offline Whisper imports use
    // whisper-cli.exe directly so AMD GPUs can accelerate inference.
    public string? WhisperCppExecutable { get; set; }
    public string? WhisperCppModel { get; set; }
    public string? WhisperCppArgumentsTemplate { get; set; }
    public string? WhisperCppLanguage { get; set; }

    /// <summary>
    /// The id of the model from Models\catalog.json that should be used for
    /// offline / file-import transcription. When set, this takes precedence
    /// over the legacy Whisper* fields.
    /// </summary>
    public string? ActiveModelId { get; set; }

    // Sentence splitting for whisper.cpp output. whisper-cli emits one JSON segment
    // per ~30 s window, and -ml N further breaks that by character count. The
    // values below are used by SplitIntoSentences to re-group the raw segments
    // into one TranscriptSegment per actual sentence (or short pause in a
    // single-speaker stream).
    public int WhisperSegmentMaxLen { get; set; } = 80;
    public bool WhisperSplitOnWord { get; set; } = true;
    public double WhisperMaxSegmentSeconds { get; set; } = 6.0;

    /// <summary>
    /// Four-second realtime windows quieter than this RMS level are discarded
    /// before launching sherpa-onnx. -50 dBFS filters digital silence and idle
    /// devices while preserving normal speech.
    /// </summary>
    public double RealtimeSilenceThresholdDb { get; set; } = -50.0;

    // Speaker diarization (sherpa-onnx pyannote-segmentation + 3dspeaker embedding).
    // When the executable + both models are present, TranscribeFileAsync will run
    // the wav file through speaker-diarization and merge the speaker labels onto
    // the Whisper-transcribed segments so the UI can show "Speaker 1 / Speaker 2"
    // instead of one undifferentiated transcript per file.
    public string? SpeakerDiarizationExecutable { get; set; }
    public string? PyannoteSegmentationModel { get; set; }
    public string? SpeakerEmbeddingModel { get; set; }

    /// <summary>
    /// Number of speaker clusters to assume. -1 means "auto" — the diarizer will
    /// use <see cref="DiarizationClusterThreshold"/> to determine the count.
    /// 0 is treated the same as -1. Set this if you know the meeting has N
    /// speakers in advance for higher accuracy.
    /// </summary>
    public int DiarizationClusterCount { get; set; } = -1;

    /// <summary>
    /// Distance threshold for the auto-cluster step (only used when
    /// <see cref="DiarizationClusterCount"/> is -1/0). Larger values produce fewer
    /// clusters (i.e. fewer speakers). 0.9 is the safer auto-mode default for
    /// conversational recordings; lower values tend to split one voice into
    /// many clusters when microphone conditions change.
    /// </summary>
    public double DiarizationClusterThreshold { get; set; } = 0.9;
}
