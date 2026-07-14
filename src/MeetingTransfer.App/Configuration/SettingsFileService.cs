using System.IO;
using System.Text.Json;
using MeetingTransfer.Core.Config;
using MeetingTransfer.Core.Models;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.App.Configuration;

public sealed class SettingsFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _baseDirectory;

    public SettingsFileService(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public string AppSettingsPath => Path.Combine(_baseDirectory, "appsettings.json");
    public string ModelsPath => Path.Combine(_baseDirectory, "models.json");
    public string AppSettingsExamplePath => Path.Combine(_baseDirectory, "appsettings.example.json");
    public string ModelsExamplePath => Path.Combine(_baseDirectory, "models.example.json");

    public RuntimeSettings Load()
    {
        EnsureWritableFiles();

        var app = ReadJson<AppOptions>(AppSettingsPath) ?? new AppOptions();
        var models = ReadJson<ModelsFile>(ModelsPath) ?? new ModelsFile();
        app.Ui ??= new UiOptions();
        app.Speech.Engine = "SherpaOnnx";

        var settings = new RuntimeSettings
        {
            App = app,
            SherpaOnnx = models.SherpaOnnx ?? new SherpaOnnxOptions(),
            Models = models
        };

        if (ApplyBuiltInSherpaDefaults(settings.SherpaOnnx) |
            ApplyDownloadedModelDefaults(settings.SherpaOnnx))
        {
            Save(settings);
        }

        if (ApplyBuiltInFfmpegDefault(settings.App.Import))
        {
            Save(settings);
        }

        if (ApplyWhisperCppActiveModelDefault(settings))
        {
            Save(settings);
        }

        // Sync the manifest-selected model id into SherpaOnnxOptions so the
        // speech engine can route to the right CLI invocation.
        if (!string.IsNullOrWhiteSpace(settings.Models.ActiveModelId))
        {
            settings.SherpaOnnx.ActiveModelId = settings.Models.ActiveModelId;
        }

        return settings;
    }

    public void Save(RuntimeSettings settings)
    {
        Directory.CreateDirectory(_baseDirectory);
        WriteJson(AppSettingsPath, settings.App);
        WriteJson(ModelsPath, new ModelsFile
        {
            SherpaOnnx = settings.SherpaOnnx,
            ActiveModelId = settings.Models?.ActiveModelId
        });
    }

    private void EnsureWritableFiles()
    {
        if (!File.Exists(AppSettingsPath))
        {
            if (File.Exists(AppSettingsExamplePath))
            {
                File.Copy(AppSettingsExamplePath, AppSettingsPath);
            }
            else
            {
                WriteJson(AppSettingsPath, new AppOptions());
            }
        }

        if (!File.Exists(ModelsPath))
        {
            if (File.Exists(ModelsExamplePath))
            {
                File.Copy(ModelsExamplePath, ModelsPath);
            }
            else
            {
                WriteJson(ModelsPath, new ModelsFile());
            }
        }
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static void WriteJson<T>(string path, T value)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);

        File.Move(tempPath, path, overwrite: true);
    }

    private bool ApplyBuiltInSherpaDefaults(SherpaOnnxOptions options)
    {
        var changed = false;

        changed |= UseBuiltInIfMissingOrInvalid(
            options.OnlineRecognizerExecutable,
            "models/sherpa-onnx/bin/sherpa-onnx-vad-with-online-asr.exe",
            value => options.OnlineRecognizerExecutable = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.OfflineRecognizerExecutable,
            "models/sherpa-onnx/bin/sherpa-onnx.exe",
            value => options.OfflineRecognizerExecutable = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.Tokens,
            "models/sherpa-onnx/models/streaming-paraformer-bilingual-zh-en/tokens.txt",
            value => options.Tokens = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.Encoder,
            "models/sherpa-onnx/models/streaming-paraformer-bilingual-zh-en/encoder.int8.onnx",
            value => options.Encoder = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.Decoder,
            "models/sherpa-onnx/models/streaming-paraformer-bilingual-zh-en/decoder.int8.onnx",
            value => options.Decoder = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.SileroVadModel,
            "models/sherpa-onnx/models/silero_vad.onnx",
            value => options.SileroVadModel = value);

        changed |= ClearMissingLegacySherpaWhisperPath(options.WhisperEncoder, value => options.WhisperEncoder = value);
        changed |= ClearMissingLegacySherpaWhisperPath(options.WhisperDecoder, value => options.WhisperDecoder = value);
        changed |= ClearMissingLegacySherpaWhisperPath(options.WhisperTokens, value => options.WhisperTokens = value);

        // Legacy sherpa Whisper fields are preserved for existing installations.
        // New installations select downloaded offline models through the catalog.
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperEncoder,
            "models/sherpa-onnx-whisper/large-v3-encoder.int8.onnx",
            value => options.WhisperEncoder = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperDecoder,
            "models/sherpa-onnx-whisper/large-v3-decoder.int8.onnx",
            value => options.WhisperDecoder = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperTokens,
            "models/sherpa-onnx-whisper/large-v3-tokens.txt",
            value => options.WhisperTokens = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperOfflineExecutable,
            "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
            value => options.WhisperOfflineExecutable = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperCppExecutable,
            "models/whisper-cpp-vulkan/whisper-cli.exe",
            value => options.WhisperCppExecutable = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.WhisperCppModel,
            "models/whisper-cpp-vulkan/models/ggml-large-v3-turbo.bin",
            value => options.WhisperCppModel = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.SpeakerDiarizationExecutable,
            "models/sherpa-onnx/bin/sherpa-onnx-offline-speaker-diarization.exe",
            value => options.SpeakerDiarizationExecutable = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.PyannoteSegmentationModel,
            "models/sherpa-onnx/models/speaker-diarization/sherpa-onnx-pyannote-segmentation-3-0/model.int8.onnx",
            value => options.PyannoteSegmentationModel = value);
        changed |= UseBuiltInIfMissingOrInvalid(
            options.SpeakerEmbeddingModel,
            "models/sherpa-onnx/models/speaker-diarization/eres2net.onnx",
            value => options.SpeakerEmbeddingModel = value);

        // Speaker count is selected per import in the main window. Do not persist
        // a fixed count from an earlier run; only retain the safer auto threshold.
        if (options.DiarizationClusterCount != -1 || options.DiarizationClusterThreshold <= 0.5)
        {
            options.DiarizationClusterCount = -1;
            options.DiarizationClusterThreshold = 0.9;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(options.WhisperCppArgumentsTemplate) ||
            !options.WhisperCppArgumentsTemplate.Contains("{Model}", StringComparison.OrdinalIgnoreCase) ||
            !options.WhisperCppArgumentsTemplate.Contains("{InputWav}", StringComparison.OrdinalIgnoreCase) ||
            !options.WhisperCppArgumentsTemplate.Contains("{Language}", StringComparison.OrdinalIgnoreCase) ||
            !options.WhisperCppArgumentsTemplate.Contains("-dev 0", StringComparison.OrdinalIgnoreCase) ||
            !options.WhisperCppArgumentsTemplate.Contains("-bs 1", StringComparison.OrdinalIgnoreCase) ||
            !options.WhisperCppArgumentsTemplate.Contains("-bo 1", StringComparison.OrdinalIgnoreCase))
        {
            options.WhisperCppArgumentsTemplate = "-m \"{Model}\" -f \"{InputWav}\" -l {Language} -t 4 -dev 0 -nfa -bs 1 -bo 1 -nt -np";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(options.WhisperCppLanguage))
        {
            options.WhisperCppLanguage = "bilingual";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(options.OnlineArgumentsTemplate) ||
            !options.OnlineArgumentsTemplate.Contains("paraformer", StringComparison.OrdinalIgnoreCase) ||
            !options.OnlineArgumentsTemplate.Contains("--num-threads=2", StringComparison.OrdinalIgnoreCase) ||
            !options.OnlineArgumentsTemplate.Contains("--blank-penalty=1.0", StringComparison.OrdinalIgnoreCase))
        {
            options.OnlineArgumentsTemplate = "--silero-vad-model=\"{SileroVadModel}\" --tokens=\"{Tokens}\" --paraformer-encoder=\"{Encoder}\" --paraformer-decoder=\"{Decoder}\" --num-threads=2 --blank-penalty=1.0 \"{InputWav}\"";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(options.OfflineArgumentsTemplate) ||
            !options.OfflineArgumentsTemplate.Contains("paraformer", StringComparison.OrdinalIgnoreCase) ||
            !options.OfflineArgumentsTemplate.Contains("--num-threads=2", StringComparison.OrdinalIgnoreCase))
        {
            options.OfflineArgumentsTemplate = "--tokens=\"{Tokens}\" --paraformer-encoder=\"{Encoder}\" --paraformer-decoder=\"{Decoder}\" --num-threads=2 \"{InputWav}\"";
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(options.Joiner))
        {
            options.Joiner = null;
            changed = true;
        }

        return changed;
    }

    private bool UseBuiltInIfMissingOrInvalid(string? currentValue, string builtInRelativePath, Action<string> setValue)
    {
        var builtInFullPath = Path.Combine(_baseDirectory, builtInRelativePath);
        if (!File.Exists(builtInFullPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(currentValue) && File.Exists(ResolvePath(currentValue)))
        {
            return false;
        }

        setValue(builtInRelativePath);
        return true;
    }

    private bool ClearMissingLegacySherpaWhisperPath(string? currentValue, Action<string?> setValue)
    {
        if (string.IsNullOrWhiteSpace(currentValue) ||
            !currentValue.Contains("models/sherpa-onnx-whisper/", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(ResolvePath(currentValue)))
        {
            return false;
        }

        setValue(null);
        return true;
    }

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(_baseDirectory, path);
    }

    private bool ApplyBuiltInFfmpegDefault(ImportOptions import)
    {
        const string builtIn = "models/ffmpeg/bin/ffmpeg.exe";
        var builtInFullPath = Path.Combine(_baseDirectory, builtIn);
        if (!File.Exists(builtInFullPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(import.FfmpegPath) && File.Exists(ResolvePath(import.FfmpegPath)))
        {
            return false;
        }

        import.FfmpegPath = builtIn;
        return true;
    }

    private bool ApplyWhisperCppActiveModelDefault(RuntimeSettings settings)
    {
        var active = settings.Models.ActiveModelId;
        var catalog = new ModelCatalog(_baseDirectory);
        var changed = false;
        if (string.Equals(active, "whisper-large-v3-int8", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(active, "whisper-large-v3-fp32", StringComparison.OrdinalIgnoreCase))
        {
            settings.Models.ActiveModelId = null;
            settings.SherpaOnnx.ActiveModelId = null;
            active = null;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(active))
        {
            var selected = catalog.FindById(active);
            if (selected is not null &&
                string.Equals(selected.ExecutionMode, "offline", StringComparison.OrdinalIgnoreCase) &&
                catalog.IsInstalled(selected))
            {
                return false;
            }

            settings.Models.ActiveModelId = null;
            settings.SherpaOnnx.ActiveModelId = null;
            active = null;
            changed = true;
        }

        var turbo = catalog.FindById("whisper-large-v3-turbo");
        if (turbo is null || !catalog.IsInstalled(turbo))
        {
            return changed;
        }

        settings.Models.ActiveModelId = "whisper-large-v3-turbo";
        settings.SherpaOnnx.ActiveModelId = settings.Models.ActiveModelId;
        return true;
    }

    private bool ApplyDownloadedModelDefaults(SherpaOnnxOptions options)
    {
        var changed = false;
        var catalog = new ModelCatalog(_baseDirectory);

        var realtime = catalog.FindById("streaming-paraformer-bilingual");
        if (realtime is not null && catalog.IsInstalled(realtime))
        {
            changed |= UseCatalogFileIfMissingOrInvalid(options.Encoder, catalog, realtime, "encoder.int8.onnx", value => options.Encoder = value);
            changed |= UseCatalogFileIfMissingOrInvalid(options.Decoder, catalog, realtime, "decoder.int8.onnx", value => options.Decoder = value);
            changed |= UseCatalogFileIfMissingOrInvalid(options.Tokens, catalog, realtime, "tokens.txt", value => options.Tokens = value);
            changed |= UseCatalogFileIfMissingOrInvalid(options.SileroVadModel, catalog, realtime, "silero_vad.onnx", value => options.SileroVadModel = value);
        }

        var diarization = catalog.FindById("speaker-diarization");
        if (diarization is not null && catalog.IsInstalled(diarization))
        {
            changed |= UseCatalogFileIfMissingOrInvalid(options.PyannoteSegmentationModel, catalog, diarization, "model.int8.onnx", value => options.PyannoteSegmentationModel = value);
            changed |= UseCatalogFileIfMissingOrInvalid(options.SpeakerEmbeddingModel, catalog, diarization, "eres2net.onnx", value => options.SpeakerEmbeddingModel = value);
        }

        return changed;
    }

    private bool UseCatalogFileIfMissingOrInvalid(
        string? currentValue,
        ModelCatalog catalog,
        ModelDescriptor model,
        string fileName,
        Action<string> setValue)
    {
        if (!string.IsNullOrWhiteSpace(currentValue) && File.Exists(ResolvePath(currentValue)))
        {
            return false;
        }

        var file = model.Files.First(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
        var path = catalog.GetInstalledFilePath(model, file);
        if (!File.Exists(path))
        {
            return false;
        }

        setValue(Path.GetRelativePath(_baseDirectory, path).Replace('\\', '/'));
        return true;
    }
}
