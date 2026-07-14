using System.IO;
using System.Text.Json;

namespace MeetingTransfer.Core.Models;

public sealed class ModelCatalog
{
    private readonly string _baseDirectory;
    private readonly string _modelsRoot;
    private readonly Lazy<List<ModelDescriptor>> _all;

    public ModelCatalog(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _modelsRoot = Path.Combine(_baseDirectory, "models");
        _all = new Lazy<List<ModelDescriptor>>(LoadFromDisk);
    }

    public IReadOnlyList<ModelDescriptor> All => _all.Value;

    public ModelDescriptor? FindById(string id)
        => _all.Value.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public string GetModelDirectory(ModelDescriptor model)
        => Path.Combine(_modelsRoot, Sanitize(model.Id));

    /// <summary>
    /// Returns the actual on-disk path of a model file. The catalog's standard
    /// location is "models/{id}/{filename}", but for the models that were
    /// bundled before the catalog existed (Whisper large-v3 int8 and the
    /// streaming paraformer), the files live under one of the legacy
    /// directories below. We probe them in order and return the first hit so
    /// existing publishes keep working without re-downloading.
    /// </summary>
    public string GetInstalledFilePath(ModelDescriptor model, ModelFileEntry file)
    {
        ValidateFileName(file.Name);
        var standard = Path.Combine(GetModelDirectory(model), file.Name);
        if (File.Exists(standard))
        {
            return standard;
        }

        // Legacy locations, ordered roughly by recency. The first match wins.
        var legacyRoots = new[]
        {
            Path.Combine(_modelsRoot, "sherpa-onnx-whisper"),  // change-07: bundled Whisper large-v3 int8
            Path.Combine(_modelsRoot, "whisper-cpp-vulkan", "models"),
            Path.Combine(_modelsRoot, "sherpa-onnx", "models", "streaming-paraformer-bilingual-zh-en"),
            Path.Combine(_modelsRoot, "sherpa-onnx", "models"),
        };
        foreach (var root in legacyRoots)
        {
            var candidate = Path.Combine(root, file.Name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to the standard path even if it doesn't exist; callers can
        // detect that and surface a clear "not installed" error.
        return standard;
    }

    public bool IsInstalled(ModelDescriptor model)
    {
        // The model is "installed" if every required file can be located —
        // either at the catalog's standard path or at one of the legacy
        // fallback locations. We don't require the model directory itself to
        // exist, since legacy bundles (change-07's Whisper large-v3 int8,
        // change-03's streaming paraformer) live elsewhere.
        foreach (var file in model.Files)
        {
            var path = GetInstalledFilePath(model, file);
            if (!File.Exists(path))
            {
                return false;
            }
        }
        return true;
    }

    public long InstalledSize(ModelDescriptor model)
    {
        var dir = GetModelDirectory(model);
        if (!Directory.Exists(dir))
        {
            return 0;
        }
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // ignore locked / vanished files
            }
        }
        return total;
    }

    public string ResolveExecutablePath(ModelDescriptor model)
        => Path.IsPathRooted(model.Executable)
            ? model.Executable
            : Path.Combine(_baseDirectory, model.Executable);

    /// <summary>
    /// Replaces templated placeholders like {Encoder}, {Decoder}, {Tokens}, {InputWav}
    /// with the installed model files / caller-supplied input wav.
    /// </summary>
    public string BuildArguments(ModelDescriptor model, string inputWav)
    {
        var args = model.ArgumentsTemplate;
        foreach (var file in model.Files)
        {
            args = ReplaceFilePlaceholders(args, file.Name, GetInstalledFilePath(model, file));
        }
        args = args.Replace("{InputWav}", inputWav, StringComparison.OrdinalIgnoreCase);
        return args;
    }

    private static string ReplaceFilePlaceholders(string args, string fileName, string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        args = args.Replace("{" + stem + "}", filePath, StringComparison.OrdinalIgnoreCase);

        foreach (var alias in GetPlaceholderAliases(fileName))
        {
            args = args.Replace("{" + alias + "}", filePath, StringComparison.OrdinalIgnoreCase);
        }

        return args;
    }

    private static IEnumerable<string> GetPlaceholderAliases(string fileName)
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

        if (string.Equals(Path.GetExtension(fileName), ".bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "model.onnx", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("modelonnx", StringComparison.Ordinal))
        {
            yield return "Model";
        }
    }

    public void DeleteInstalled(ModelDescriptor model)
    {
        var dir = GetModelDirectory(model);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public bool CanDeleteInstalled(ModelDescriptor model)
        => Directory.Exists(GetModelDirectory(model));

    private List<ModelDescriptor> LoadFromDisk()
    {
        var path = Path.Combine(_baseDirectory, "Models", "catalog.json");
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<ModelCatalogFile>(json, JsonOptions);
            return file?.Models ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray();
        var s = new string(chars);
        if (string.IsNullOrWhiteSpace(s) || s is "." or "..")
        {
            s = "_model";
        }
        return s.Length > 80 ? s[..80] : s;
    }

    private static void ValidateFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            !string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) ||
            value is "." or "..")
        {
            throw new InvalidDataException($"Unsafe model file name: '{value}'.");
        }
    }
}
