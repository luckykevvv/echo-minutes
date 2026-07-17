using System.IO;
using System.Text.Json;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.Tests;

public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _tempDir;

    public ModelCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mt-catalog-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ModelDescriptor MakeModel(string id, params string[] fileNames)
        => new()
        {
            Id = id,
            Family = "Test",
            DisplayName = id,
            SizeBytes = 100,
            Executable = "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
            Files = fileNames.Select(n => new ModelFileEntry { Name = n, Url = "https://example.com/" + n }).ToList(),
            ArgumentsTemplate = "--whisper-encoder=\"{Encoder}\" --whisper-decoder=\"{Decoder}\" --tokens=\"{Tokens}\" \"{InputWav}\""
        };

    private void WriteCatalog(params ModelDescriptor[] models)
    {
        var file = new ModelCatalogFile { Version = 1, Models = models.ToList() };
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.Combine(_tempDir, "Models");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "catalog.json"), json);
    }

    [Fact]
    public void Catalog_LoadsAllEntries()
    {
        WriteCatalog(MakeModel("a", "x.onnx"), MakeModel("b", "y.onnx"));
        var catalog = new ModelCatalog(_tempDir);
        Assert.Equal(2, catalog.All.Count);
        Assert.NotNull(catalog.FindById("a"));
        Assert.NotNull(catalog.FindById("b"));
        Assert.Null(catalog.FindById("missing"));
    }

    [Fact]
    public void IsInstalled_RequiresAllFilesPresent()
    {
        var model = MakeModel("m", "encoder.onnx", "decoder.onnx", "tokens.txt");
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        Assert.False(catalog.IsInstalled(model));

        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "encoder.onnx"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(dir, "decoder.onnx"), [1, 2, 3]);
        Assert.False(catalog.IsInstalled(model));  // tokens still missing

        File.WriteAllBytes(Path.Combine(dir, "tokens.txt"), [1, 2, 3]);
        Assert.True(catalog.IsInstalled(model));
    }

    [Fact]
    public void BuildArguments_ReplacesFileStemsAndInputWav()
    {
        var model = MakeModel("m", "encoder.onnx", "decoder.onnx", "tokens.txt");
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "encoder.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "decoder.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "tokens.txt"), [1]);

        var args = catalog.BuildArguments(model, "C:/input.wav");
        Assert.Contains("\"C:/input.wav\"", args);
        Assert.Contains("encoder.onnx", args);
        Assert.Contains("decoder.onnx", args);
        Assert.Contains("tokens.txt", args);
        Assert.DoesNotContain("{Encoder}", args);
        Assert.DoesNotContain("{InputWav}", args);
    }

    [Fact]
    public void BuildArguments_ReplacesCommonAliasesForVersionedModelFiles()
    {
        var model = MakeModel(
            "whisper-large-v3-int8",
            "large-v3-encoder.int8.onnx",
            "large-v3-decoder.int8.onnx",
            "large-v3-tokens.txt");
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        foreach (var file in model.Files)
        {
            File.WriteAllBytes(Path.Combine(dir, file.Name), [1]);
        }

        var args = catalog.BuildArguments(model, "C:/input.wav");

        Assert.Contains("large-v3-encoder.int8.onnx", args);
        Assert.Contains("large-v3-decoder.int8.onnx", args);
        Assert.Contains("large-v3-tokens.txt", args);
        Assert.DoesNotContain("{Encoder}", args);
        Assert.DoesNotContain("{Decoder}", args);
        Assert.DoesNotContain("{Tokens}", args);
    }

    [Fact]
    public void BuildArguments_ReplacesModelAndConvFrontendAliases()
    {
        var model = new ModelDescriptor
        {
            Id = "qwen",
            Family = "Test",
            DisplayName = "qwen",
            SizeBytes = 100,
            Executable = "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
            Files =
            [
                new ModelFileEntry { Name = "qwen3-asr-conv-frontend.onnx", Url = "https://example.com/frontend" },
                new ModelFileEntry { Name = "model.int8.onnx", Url = "https://example.com/model" },
                new ModelFileEntry { Name = "tokens.txt", Url = "https://example.com/tokens" },
            ],
            ArgumentsTemplate = "--qwen3-asr-conv-frontend=\"{ConvFrontend}\" --sense-voice-model=\"{Model}\" --tokens=\"{Tokens}\" \"{InputWav}\""
        };
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        foreach (var file in model.Files)
        {
            File.WriteAllBytes(Path.Combine(dir, file.Name), [1]);
        }

        var args = catalog.BuildArguments(model, "C:/input.wav");

        Assert.Contains("qwen3-asr-conv-frontend.onnx", args);
        Assert.Contains("model.int8.onnx", args);
        Assert.Contains("tokens.txt", args);
        Assert.DoesNotContain("{ConvFrontend}", args);
        Assert.DoesNotContain("{Model}", args);
        Assert.DoesNotContain("{Tokens}", args);
    }

    [Fact]
    public void BuildArguments_ReplacesModelAliasForGgmlBin()
    {
        var model = new ModelDescriptor
        {
            Id = "whisper-large-v3-turbo",
            Family = "Whisper.cpp",
            DisplayName = "Whisper large-v3 turbo",
            SizeBytes = 100,
            Executable = "models/whisper-cpp-vulkan/whisper-cli.exe",
            Files =
            [
                new ModelFileEntry { Name = "ggml-large-v3-turbo.bin", Url = "https://example.com/model" },
            ],
            ArgumentsTemplate = "-m \"{Model}\" -f \"{InputWav}\" -l {Language}"
        };
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "ggml-large-v3-turbo.bin"), [1]);

        var args = catalog.BuildArguments(model, "C:/input.wav");

        Assert.Contains("ggml-large-v3-turbo.bin", args);
        Assert.Contains("\"C:/input.wav\"", args);
        Assert.DoesNotContain("{Model}", args);
        Assert.Contains("{Language}", args);
    }

    [Fact]
    public void DeleteInstalled_RemovesModelDirectory()
    {
        var model = MakeModel("m", "encoder.onnx");
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "encoder.onnx"), [1]);

        Assert.True(Directory.Exists(dir));
        catalog.DeleteInstalled(model);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void GetInstalledFilePath_RejectsDirectoryTraversal()
    {
        var model = MakeModel("safe", "model.onnx");
        var catalog = new ModelCatalog(_tempDir);
        var unsafeFile = new ModelFileEntry
        {
            Name = "../outside.bin",
            Url = "https://example.com/outside.bin"
        };

        Assert.Throws<InvalidDataException>(() => catalog.GetInstalledFilePath(model, unsafeFile));
    }

    [Fact]
    public void CanDeleteInstalled_IsFalseForBundledLegacyModel()
    {
        var model = MakeModel("whisper-large-v3-turbo", "ggml-large-v3-turbo.bin");
        var bundledDir = Path.Combine(_tempDir, "models", "whisper-cpp-vulkan", "models");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllBytes(Path.Combine(bundledDir, "ggml-large-v3-turbo.bin"), [1]);
        var catalog = new ModelCatalog(_tempDir);

        Assert.True(catalog.IsInstalled(model));
        Assert.False(catalog.CanDeleteInstalled(model));
    }

    [Fact]
    public void Catalog_HandlesMissingFileGracefully()
    {
        // no catalog written
        var catalog = new ModelCatalog(_tempDir);
        Assert.Empty(catalog.All);
    }

    [Fact]
    public void Catalog_ReportsBackendFieldPerModel()
    {
        // Each model card needs a Backend field surfaced to the UI so users
        // can see which models run on the GPU (whisper.cpp Vulkan) and which
        // still fall back to CPU (SenseVoice / Paraformer / Qwen3-ASR / streaming).
        var gpu = MakeModel("whisper-tiny.en", "tiny.en-encoder.onnx");
        gpu.Backend = "GPU";
        var cpu = MakeModel("sense-voice-small", "model.onnx");
        cpu.Backend = "CPU";
        WriteCatalog(gpu, cpu);
        var catalog = new ModelCatalog(_tempDir);

        var loadedGpu = catalog.FindById("whisper-tiny.en")!;
        var loadedCpu = catalog.FindById("sense-voice-small")!;
        Assert.Equal("GPU", loadedGpu.Backend);
        Assert.Equal("CPU", loadedCpu.Backend);
    }

    [Fact]
    public void Catalog_DefaultsBackendToCpuWhenMissing()
    {
        // Forward-compat: a catalog written before this field existed must
        // still report something sensible (CPU) rather than empty string.
        var model = MakeModel("legacy", "model.onnx");
        // do NOT set Backend
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var loaded = catalog.FindById("legacy")!;
        Assert.Equal("CPU", loaded.Backend);
    }

    [Fact]
    public void EngineDisplay_UsesCatalogValueAndFallsBackToExecutable()
    {
        var explicitEngine = new ModelDescriptor
        {
            Engine = "custom-runtime",
            Executable = "models/sherpa-onnx/bin/sherpa-onnx.exe",
        };
        var whisperFallback = new ModelDescriptor
        {
            Executable = "models/whisper-cpp-vulkan/whisper-cli.exe",
        };
        var sherpaFallback = new ModelDescriptor
        {
            Executable = "models/sherpa-onnx/bin/sherpa-onnx-offline.exe",
        };

        Assert.Equal("custom-runtime", explicitEngine.EngineDisplay);
        Assert.Equal("whisper.cpp", whisperFallback.EngineDisplay);
        Assert.Equal("sherpa-onnx", sherpaFallback.EngineDisplay);
    }

    [Fact]
    public void IsInstalled_FallsBackToLegacyPaths()
    {
        // Simulate the change-07 layout where Whisper large-v3 int8 was bundled
        // directly under models/sherpa-onnx-whisper/ rather than under the
        // catalog's standard models/whisper-large-v3-int8/ directory.
        var model = MakeModel("whisper-large-v3-int8",
            "large-v3-encoder.int8.onnx",
            "large-v3-decoder.int8.onnx",
            "large-v3-tokens.txt");
        WriteCatalog(model);

        var legacyDir = Path.Combine(_tempDir, "models", "sherpa-onnx-whisper");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllBytes(Path.Combine(legacyDir, "large-v3-encoder.int8.onnx"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(legacyDir, "large-v3-decoder.int8.onnx"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(legacyDir, "large-v3-tokens.txt"), [1, 2, 3]);

        var catalog = new ModelCatalog(_tempDir);
        Assert.True(catalog.IsInstalled(model));
        Assert.Equal(
            Path.Combine(legacyDir, "large-v3-encoder.int8.onnx"),
            catalog.GetInstalledFilePath(model, model.Files[0]));
    }

    [Fact]
    public void IsInstalled_PrefersStandardPathOverLegacy()
    {
        // If both locations exist, the standard one wins so the catalog's own
        // downloads don't get shadowed by leftover legacy files.
        var model = MakeModel("m", "encoder.onnx");
        WriteCatalog(model);
        var catalog = new ModelCatalog(_tempDir);
        var standardDir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(standardDir);
        File.WriteAllBytes(Path.Combine(standardDir, "encoder.onnx"), [42]);
        var legacyDir = Path.Combine(_tempDir, "models", "sherpa-onnx-whisper");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllBytes(Path.Combine(legacyDir, "encoder.onnx"), [1]);

        var path = catalog.GetInstalledFilePath(model, model.Files[0]);
        Assert.EndsWith("m\\encoder.onnx", path);
        Assert.Equal(42, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void IsInstalled_FallsBackToBundledWhisperCppModel()
    {
        var model = new ModelDescriptor
        {
            Id = "whisper-large-v3-turbo",
            Family = "Whisper.cpp",
            DisplayName = "Whisper large-v3 turbo",
            SizeBytes = 100,
            Executable = "models/whisper-cpp-vulkan/whisper-cli.exe",
            Files =
            [
                new ModelFileEntry { Name = "ggml-large-v3-turbo.bin", Url = "https://example.com/model" },
            ],
            ArgumentsTemplate = "-m \"{Model}\" -f \"{InputWav}\""
        };
        WriteCatalog(model);

        var bundledDir = Path.Combine(_tempDir, "models", "whisper-cpp-vulkan", "models");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllBytes(Path.Combine(bundledDir, "ggml-large-v3-turbo.bin"), [1, 2, 3]);

        var catalog = new ModelCatalog(_tempDir);
        Assert.True(catalog.IsInstalled(model));
        Assert.Equal(
            Path.Combine(bundledDir, "ggml-large-v3-turbo.bin"),
            catalog.GetInstalledFilePath(model, model.Files[0]));
    }
}
