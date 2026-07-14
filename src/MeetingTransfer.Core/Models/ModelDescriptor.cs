using System.Text.Json.Serialization;

namespace MeetingTransfer.Core.Models;

public sealed class ModelCatalogFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("models")]
    public List<ModelDescriptor> Models { get; set; } = [];
}

public sealed class ModelDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("family")]
    public string Family { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    [JsonPropertyName("task")]
    public string Task { get; set; } = "transcribe";

    /// <summary>"offline" or "online" (streaming).</summary>
    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; set; } = "offline";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("speedNote")]
    public string SpeedNote { get; set; } = "";

    [JsonPropertyName("accuracyNote")]
    public string AccuracyNote { get; set; } = "";

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "";

    /// <summary>The runtime that executes this model, e.g. whisper.cpp or sherpa-onnx.</summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonIgnore]
    public string EngineDisplay => !string.IsNullOrWhiteSpace(Engine)
        ? Engine
        : Executable.Contains("whisper", StringComparison.OrdinalIgnoreCase)
            ? "whisper.cpp"
            : Executable.Contains("sherpa-onnx", StringComparison.OrdinalIgnoreCase)
                ? "sherpa-onnx"
                : "Unknown";

    [JsonPropertyName("files")]
    public List<ModelFileEntry> Files { get; set; } = [];

    [JsonPropertyName("argumentsTemplate")]
    public string ArgumentsTemplate { get; set; } = "";

    /// <summary>
    /// "GPU" (whisper.cpp Vulkan / sherpa-onnx DML/CUDA), or "CPU" (current default
    /// for non-Whisper models in this project). Surfaces to the model card so users
    /// know whether they will get hardware acceleration or not.
    /// </summary>
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "CPU";
}

public sealed class ModelFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("extract")]
    public ModelFileExtract? Extract { get; set; }
}

public sealed class ModelFileExtract
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "tar.bz2";

    [JsonPropertyName("member")]
    public string Member { get; set; } = "";
}
