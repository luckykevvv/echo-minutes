using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace EchoMinutes.Updater;

internal static class Program
{
    private const int MaximumArchiveEntries = 10_000;
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.json",
        "models.json",
        "data",
        "recordings",
        "exports"
    };

    [STAThread]
    private static int Main(string[] args)
    {
        string? targetDirectory = null;
        string? appExecutable = null;
        string? packagePath = null;
        var language = CultureInfo.CurrentUICulture.Name;
        var restartApplication = true;
        try
        {
            var options = ParseArguments(args);
            if (options.TryGetValue("language", out var requestedLanguage))
            {
                language = requestedLanguage;
            }
            packagePath = Path.GetFullPath(Required(options, "package"));
            targetDirectory = Path.GetFullPath(Required(options, "target"));
            appExecutable = Path.GetFullPath(Required(options, "app"));
            var processId = int.Parse(Required(options, "pid"));
            restartApplication = !options.TryGetValue("restart", out var restartValue) ||
                !string.Equals(restartValue, "false", StringComparison.OrdinalIgnoreCase);

            WaitForProcess(processId);
            ApplyPackage(packagePath, targetDirectory);
            if (restartApplication)
            {
                Process.Start(new ProcessStartInfo(appExecutable) { UseShellExecute = true });
            }
            return 0;
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(Path.GetTempPath(), $"EchoMinutes-update-error-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(logPath, ex.ToString());
            var failure = BuildFailureMessage(language, ex.Message, logPath);
            MessageBox(IntPtr.Zero, failure.Text, failure.Caption, 0x10);
            if (restartApplication && !string.IsNullOrWhiteSpace(appExecutable) && File.Exists(appExecutable))
            {
                Process.Start(new ProcessStartInfo(appExecutable) { UseShellExecute = true });
            }

            return 1;
        }
        finally
        {
            if (packagePath is not null)
            {
                TryCleanupDownloadedPackage(packagePath);
            }
        }
    }

    private static void WaitForProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(60_000))
            {
                throw new TimeoutException("The running application did not close within 60 seconds.");
            }
        }
        catch (ArgumentException)
        {
            // The application already exited.
        }
    }

    internal static void ApplyPackage(string packagePath, string targetDirectory)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("The downloaded update package was not found.", packagePath);
        }

        Directory.CreateDirectory(targetDirectory);
        var workDirectory = Path.Combine(Path.GetTempPath(), "EchoMinutes", "apply", Guid.NewGuid().ToString("N"));
        var stagingDirectory = Path.Combine(workDirectory, "staging");
        var backupDirectory = Path.Combine(workDirectory, "backup");
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);

        try
        {
            ValidateArchive(packagePath, stagingDirectory);
            ZipFile.ExtractToDirectory(packagePath, stagingDirectory, overwriteFiles: true);
            var contentRoot = FindContentRoot(stagingDirectory);
            var copiedFiles = new List<(string Target, string? Backup)>();
            try
            {
                foreach (var sourceFile in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(contentRoot, sourceFile);
                    if (IsProtected(relativePath))
                    {
                        continue;
                    }

                    var targetFile = SafeCombine(targetDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    string? backupFile = null;
                    if (File.Exists(targetFile))
                    {
                        backupFile = SafeCombine(backupDirectory, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                        File.Copy(targetFile, backupFile, overwrite: true);
                    }

                    copiedFiles.Add((targetFile, backupFile));
                    File.Copy(sourceFile, targetFile, overwrite: true);
                }
            }
            catch
            {
                foreach (var item in copiedFiles.AsEnumerable().Reverse())
                {
                    if (item.Backup is not null && File.Exists(item.Backup))
                    {
                        File.Copy(item.Backup, item.Target, overwrite: true);
                    }
                    else if (File.Exists(item.Target))
                    {
                        File.Delete(item.Target);
                    }
                }

                throw;
            }
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Temporary cleanup is best effort.
            }
        }
    }

    private static void ValidateArchive(string packagePath, string stagingDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException("The update package contains an invalid number of entries.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                _ = SafeCombine(stagingDirectory, relativePath);
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The expanded update package is too large.");
            }
        }
    }

    internal static void TryCleanupDownloadedPackage(string packagePath)
    {
        try
        {
            var updatesRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EchoMinutes", "updates"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var packageDirectory = Path.GetFullPath(Path.GetDirectoryName(packagePath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentDirectory = Path.GetDirectoryName(packageDirectory)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (!string.Equals(parentDirectory, updatesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(packageDirectory))
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }
        catch
        {
            // Temporary cleanup is best effort and must never hide update results.
        }
    }

    private static string FindContentRoot(string stagingDirectory)
    {
        if (File.Exists(Path.Combine(stagingDirectory, "MeetingTransfer.App.exe")))
        {
            return stagingDirectory;
        }

        var candidates = Directory.EnumerateFiles(stagingDirectory, "MeetingTransfer.App.exe", SearchOption.AllDirectories).ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidDataException("The update package does not contain exactly one MeetingTransfer.App.exe.");
        }

        return Path.GetDirectoryName(candidates[0])!;
    }

    private static bool IsProtected(string relativePath)
    {
        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return ProtectedRoots.Contains(firstSegment);
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update package contains an unsafe path.");
        }

        return fullPath;
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index].TrimStart('-', '/')] = args[index + 1];
        }

        return values;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{key} argument.");

    internal static (string Caption, string Text) BuildFailureMessage(
        string? language,
        string error,
        string logPath)
    {
        if (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (
                "更新失败",
                $"EchoMinutes 无法完成更新。\n\n{error}\n\n详细日志：{logPath}");
        }

        return (
            "Update failed",
            $"EchoMinutes could not be updated.\n\n{error}\n\nDetails: {logPath}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
