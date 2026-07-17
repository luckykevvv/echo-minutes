namespace MeetingTransfer.Core.Config;

public sealed class AppOptions
{
    public StorageOptions Storage { get; set; } = new();
    public ImportOptions Import { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public SpeechOptions Speech { get; set; } = new();
    public UiOptions Ui { get; set; } = new();
}

public sealed class UiOptions
{
    public bool OnboardingCompleted { get; set; }
    public string Language { get; set; } = "zh-CN";
}

public sealed class StorageOptions
{
    public string DatabasePath { get; set; } = "data/meeting-transfer.sqlite";
    public string RecordingsDirectory { get; set; } = "recordings";
    public string ExportsDirectory { get; set; } = "exports";
}

public sealed class ImportOptions
{
    public string? FfmpegPath { get; set; }
}

public sealed class AudioOptions
{
    public int ChunkMilliseconds { get; set; } = 200;
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
}

public sealed class SpeechOptions
{
    public string Engine { get; set; } = "SherpaOnnx";
}
