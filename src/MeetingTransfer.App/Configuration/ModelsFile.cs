using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.App.Configuration;

public sealed class ModelsFile
{
    public SherpaOnnxOptions SherpaOnnx { get; set; } = new();

    /// <summary>
    /// The id of the model from Models\catalog.json that should be used for
    /// offline / file-import transcription. When set, this takes precedence
    /// over the legacy SherpaOnnx.Whisper* fields.
    /// </summary>
    public string? ActiveModelId { get; set; }
}
