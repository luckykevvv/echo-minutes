using System.IO;
using System.IO.Compression;
using EchoMinutes.Updater;

namespace MeetingTransfer.App.SmokeTests;

public sealed class UpdaterPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "echo-minutes-updater-tests-" + Guid.NewGuid().ToString("N"));

    public UpdaterPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void ApplyPackage_UpdatesApplicationButPreservesUserData()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        WriteText(Path.Combine(target, "MeetingTransfer.App.exe"), "old app");
        WriteText(Path.Combine(target, "appsettings.json"), "personal settings");
        WriteText(Path.Combine(target, "models.json"), "personal models");
        WriteText(Path.Combine(target, "data", "meeting.db"), "personal database");

        var package = Path.Combine(_root, "update.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            AddText(archive, "MeetingTransfer.App.exe", "new app");
            AddText(archive, "MeetingTransfer.App.dll", "new assembly");
            AddText(archive, "appsettings.json", "packaged settings");
            AddText(archive, "models.json", "packaged models");
            AddText(archive, "data/meeting.db", "packaged database");
        }

        Program.ApplyPackage(package, target);

        Assert.Equal("new app", File.ReadAllText(Path.Combine(target, "MeetingTransfer.App.exe")));
        Assert.Equal("new assembly", File.ReadAllText(Path.Combine(target, "MeetingTransfer.App.dll")));
        Assert.Equal("personal settings", File.ReadAllText(Path.Combine(target, "appsettings.json")));
        Assert.Equal("personal models", File.ReadAllText(Path.Combine(target, "models.json")));
        Assert.Equal("personal database", File.ReadAllText(Path.Combine(target, "data", "meeting.db")));
    }

    [Fact]
    public void ApplyPackage_RejectsPathTraversal()
    {
        var target = Path.Combine(_root, "target");
        var package = Path.Combine(_root, "unsafe.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            AddText(archive, "MeetingTransfer.App.exe", "new app");
            AddText(archive, "../escape.txt", "must not escape");
        }

        Assert.Throws<InvalidDataException>(() => Program.ApplyPackage(package, target));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void Cleanup_DoesNotDeleteAnUnownedPackageDirectory()
    {
        var packageDirectory = Path.Combine(_root, "user-folder");
        var package = Path.Combine(packageDirectory, "update.zip");
        WriteText(package, "not an archive");
        WriteText(Path.Combine(packageDirectory, "keep.txt"), "keep me");

        Program.TryCleanupDownloadedPackage(package);

        Assert.True(File.Exists(package));
        Assert.True(File.Exists(Path.Combine(packageDirectory, "keep.txt")));
    }

    [Theory]
    [InlineData("zh-CN", "更新失败", "无法完成更新", "详细日志")]
    [InlineData("en-US", "Update failed", "could not be updated", "Details")]
    public void FailureMessage_FollowsSelectedLanguage(
        string language,
        string expectedCaption,
        string expectedMessage,
        string expectedDetailsLabel)
    {
        var failure = Program.BuildFailureMessage(language, "technical error", "update.log");

        Assert.Equal(expectedCaption, failure.Caption);
        Assert.Contains(expectedMessage, failure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedDetailsLabel, failure.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("technical error", failure.Text, StringComparison.Ordinal);
        Assert.Contains("update.log", failure.Text, StringComparison.Ordinal);
    }

    private static void AddText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
