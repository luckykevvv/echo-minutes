using MeetingTransfer.Core.Config;

namespace MeetingTransfer.Tests;

public sealed class StoragePathResolverTests
{
    [Fact]
    public void Resolve_AnchorsRelativePathsToApplicationDirectory()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "echo-minutes-app");
        var storage = new StorageOptions
        {
            DatabasePath = "data/meeting-transfer.sqlite",
            RecordingsDirectory = "recordings",
            ExportsDirectory = "exports",
            LogDirectory = "data/logs"
        };

        StoragePathResolver.Resolve(storage, applicationDirectory);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(applicationDirectory, "data/meeting-transfer.sqlite")),
            storage.DatabasePath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(applicationDirectory, "recordings")),
            storage.RecordingsDirectory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(applicationDirectory, "exports")),
            storage.ExportsDirectory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(applicationDirectory, "data/logs")),
            storage.LogDirectory);
    }

    [Fact]
    public void Resolve_PreservesAbsolutePaths()
    {
        var customRoot = Path.Combine(Path.GetTempPath(), "echo-minutes-custom");
        var storage = new StorageOptions
        {
            DatabasePath = Path.Combine(customRoot, "meeting.sqlite"),
            RecordingsDirectory = Path.Combine(customRoot, "recordings"),
            ExportsDirectory = Path.Combine(customRoot, "exports"),
            LogDirectory = Path.Combine(customRoot, "logs")
        };

        StoragePathResolver.Resolve(storage, Path.Combine(Path.GetTempPath(), "other-app"));

        Assert.Equal(Path.GetFullPath(Path.Combine(customRoot, "meeting.sqlite")), storage.DatabasePath);
        Assert.Equal(Path.GetFullPath(Path.Combine(customRoot, "recordings")), storage.RecordingsDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(customRoot, "exports")), storage.ExportsDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(customRoot, "logs")), storage.LogDirectory);
    }
}
