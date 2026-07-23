namespace MeetingTransfer.Core.Config;

public static class StoragePathResolver
{
    public static void Resolve(StorageOptions storage, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        var root = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        storage.DatabasePath = ResolvePath(storage.DatabasePath, root);
        storage.RecordingsDirectory = ResolvePath(storage.RecordingsDirectory, root);
        storage.ExportsDirectory = ResolvePath(storage.ExportsDirectory, root);
        storage.LogDirectory = ResolvePath(storage.LogDirectory, root);
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Storage paths cannot be empty.");
        }

        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));
    }
}
