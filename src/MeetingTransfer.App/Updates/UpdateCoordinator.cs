using System.Diagnostics;
using System.IO;
using System.Windows;
using MeetingTransfer.Core.Updates;

namespace MeetingTransfer.App.Updates;

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,
    InstallStarted,
    Failed
}

public sealed class UpdateCoordinator
{
    private static readonly SemaphoreSlim CheckLock = new(1, 1);
    private readonly GitHubReleaseClient _releaseClient;

    public UpdateCoordinator(GitHubReleaseClient? releaseClient = null)
    {
        _releaseClient = releaseClient ?? new GitHubReleaseClient();
    }

    public static string CurrentVersionText => $"v{GitHubReleaseClient.CurrentVersion.ToString(3)}";

    public async Task<UpdateCheckOutcome> CheckAndPromptAsync(
        Window owner,
        bool showUpToDate,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CheckLock.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            status?.Invoke("An update check is already running.");
            return UpdateCheckOutcome.Failed;
        }

        try
        {
            status?.Invoke("Checking GitHub Releases…");
            var release = await _releaseClient.CheckForUpdateAsync(cancellationToken).ConfigureAwait(true);
            if (release is null)
            {
                status?.Invoke($"{CurrentVersionText} is up to date.");
                if (showUpToDate)
                {
                    MessageBox.Show(owner, "You already have the latest version.", "EchoMinutes update", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return UpdateCheckOutcome.UpToDate;
            }

            status?.Invoke($"{release.TagName} is available.");
            var updateWindow = new UpdateWindow(release, _releaseClient) { Owner = owner };
            updateWindow.ShowDialog();
            if (!updateWindow.InstallReady)
            {
                return UpdateCheckOutcome.UpdateAvailable;
            }

            LaunchUpdater(updateWindow.PackagePath!);
            status?.Invoke("Update downloaded. Closing EchoMinutes…");
            if (owner != Application.Current.MainWindow)
            {
                owner.Close();
            }

            Application.Current.MainWindow?.Close();
            return UpdateCheckOutcome.InstallStarted;
        }
        catch (OperationCanceledException)
        {
            status?.Invoke("Update check cancelled.");
            return UpdateCheckOutcome.Failed;
        }
        catch (Exception ex)
        {
            status?.Invoke("Could not check for updates.");
            if (showUpToDate)
            {
                MessageBox.Show(owner, ex.Message, "Update check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return UpdateCheckOutcome.Failed;
        }
        finally
        {
            CheckLock.Release();
        }
    }

    private static void LaunchUpdater(string packagePath)
    {
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "Updater");
        var updaterExecutable = Path.Combine(sourceDirectory, "EchoMinutes.Updater.exe");
        if (!File.Exists(updaterExecutable))
        {
            throw new FileNotFoundException("The EchoMinutes updater is missing from this installation.", updaterExecutable);
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "EchoMinutes", "updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(sourceFile, Path.Combine(temporaryDirectory, Path.GetFileName(sourceFile)), overwrite: true);
        }

        var temporaryUpdater = Path.Combine(temporaryDirectory, "EchoMinutes.Updater.exe");
        var appExecutable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MeetingTransfer.App.exe");
        var arguments = string.Join(' ',
            "--package", Quote(packagePath),
            "--target", Quote(AppContext.BaseDirectory),
            "--app", Quote(appExecutable),
            "--pid", Environment.ProcessId.ToString());
        Process.Start(new ProcessStartInfo(temporaryUpdater, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = temporaryDirectory
        });
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
