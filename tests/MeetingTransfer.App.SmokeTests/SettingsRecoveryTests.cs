using System.IO;
using System.Text.Json;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Diagnostics;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Audio;
using MeetingTransfer.Core.Audio;

namespace MeetingTransfer.App.SmokeTests;

public sealed class SettingsRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "echo-minutes-settings-recovery-" + Guid.NewGuid().ToString("N"));

    public SettingsRecoveryTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "appsettings.example.json"),
            """{"Storage":{"DatabasePath":"data/test.sqlite","RecordingsDirectory":"recordings","ExportsDirectory":"exports"},"Import":{},"Audio":{},"Speech":{},"Ui":{"Language":"en-US"}}""");
        File.WriteAllText(Path.Combine(_directory, "models.example.json"),
            """{"SherpaOnnx":{},"ActiveModelId":null}""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void Load_BacksUpAndRecoversMalformedConfigurationFiles()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), "{ definitely broken");
        File.WriteAllText(Path.Combine(_directory, "models.json"), "null");
        var service = new SettingsFileService(_directory);

        var settings = service.Load();

        Assert.Equal("en-US", settings.App.Ui.Language);
        Assert.NotNull(settings.SherpaOnnx);
        Assert.Equal(2, service.RecoveredFiles.Count);
        Assert.All(service.RecoveredFiles, path => Assert.True(File.Exists(path)));
        Assert.All(service.RecoveredFiles, path => Assert.Contains(".broken-", path, StringComparison.Ordinal));
        Assert.NotNull(JsonDocument.Parse(File.ReadAllText(service.AppSettingsPath)));
        Assert.NotNull(JsonDocument.Parse(File.ReadAllText(service.ModelsPath)));
    }

    [Fact]
    public void Load_NormalizesNullAndOutOfRangeOptions()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"),
            """{"Storage":null,"Import":null,"Audio":{"SampleRate":0,"Channels":8,"ChunkMilliseconds":1},"Speech":null,"Ui":null}""");
        File.WriteAllText(Path.Combine(_directory, "models.json"),
            """{"SherpaOnnx":null}""");
        var service = new SettingsFileService(_directory);

        var settings = service.Load();

        Assert.Equal("data/meeting-transfer.sqlite", settings.App.Storage.DatabasePath);
        Assert.Equal(16000, settings.App.Audio.SampleRate);
        Assert.Equal(1, settings.App.Audio.Channels);
        Assert.Equal(200, settings.App.Audio.ChunkMilliseconds);
        Assert.Equal("zh-CN", settings.App.Ui.Language);
        Assert.NotNull(settings.SherpaOnnx);
        Assert.Empty(service.RecoveredFiles);
    }

    [Fact]
    public async Task RelayCommand_RoutesAsyncFailureWithoutRethrowingOnUiThread()
    {
        Exception? routed = null;
        var command = new RelayCommand(
            () => throw new InvalidOperationException("expected failure"),
            onError: error => routed = error);

        await command.ExecuteAsync(null);

        var error = Assert.IsType<InvalidOperationException>(routed);
        Assert.Equal("expected failure", error.Message);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void PcmSessionRecorder_PreservesTrackIdentityForOfflineRefinement()
    {
        var recordingDirectory = Path.Combine(_directory, "recordings");
        using var recorder = new PcmSessionRecorder(recordingDirectory);
        recorder.Write(new PcmAudioChunk(
            "microphone-id",
            AudioSourceKind.Microphone,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            16000,
            1,
            new byte[320]));

        var track = Assert.Single(recorder.RecordedTracks);
        Assert.Equal("microphone-id", track.SourceId);
        Assert.Equal(AudioSourceKind.Microphone, track.SourceKind);
        Assert.True(File.Exists(track.Path));
        Assert.True(new FileInfo(track.Path).Length > 44);
    }

    [Fact]
    public void DiagnosticLog_WritesLocalVersionAndErrorEvidence()
    {
        var logDirectory = Path.Combine(_directory, "logs");
        DiagnosticLog.Initialize(logDirectory);
        DiagnosticLog.Info("runtime probe");
        DiagnosticLog.Error("expected diagnostic", new InvalidOperationException("probe failure"));

        var path = Assert.IsType<string>(DiagnosticLog.Path);
        var text = File.ReadAllText(path);
        Assert.Contains("EchoMinutes v", text, StringComparison.Ordinal);
        Assert.Contains("runtime probe", text, StringComparison.Ordinal);
        Assert.Contains("expected diagnostic", text, StringComparison.Ordinal);
        Assert.Contains("probe failure", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticLog_UnwritablePathDoesNotPreventStartup()
    {
        var filePath = Path.Combine(_directory, "occupied-by-a-file");
        File.WriteAllText(filePath, "not a directory");

        var error = Record.Exception(() => DiagnosticLog.Initialize(filePath));

        Assert.Null(error);
        Assert.Null(DiagnosticLog.Path);
    }
}
