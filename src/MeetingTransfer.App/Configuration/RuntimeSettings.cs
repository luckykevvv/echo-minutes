using MeetingTransfer.Core.Config;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.App.Configuration;

public sealed class RuntimeSettings
{
    public AppOptions App { get; set; } = new();
    public SherpaOnnxOptions SherpaOnnx { get; set; } = new();
    public ModelsFile Models { get; set; } = new();
}
