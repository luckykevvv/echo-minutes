using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using MeetingTransfer.App.Localization;
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

    public static string CurrentVersionText
    {
        get
        {
            var informationalVersion = typeof(GitHubReleaseClient).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+')[0];
            return $"v{(string.IsNullOrWhiteSpace(informationalVersion) ? GitHubReleaseClient.CurrentVersion.ToString(3) : informationalVersion)}";
        }
    }

    public async Task<UpdateCheckOutcome> CheckAndPromptAsync(
        Window owner,
        bool showUpToDate,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CheckLock.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            status?.Invoke(LocalizationManager.Text("UpdateCheckRunning"));
            return UpdateCheckOutcome.Failed;
        }

        try
        {
            status?.Invoke(LocalizationManager.Text("UpdateChecking"));
            var release = await _releaseClient.CheckForUpdateAsync(cancellationToken).ConfigureAwait(true);
            if (release is null)
            {
                status?.Invoke(LocalizationManager.Format("UpdateUpToDateStatus", CurrentVersionText));
                if (showUpToDate)
                {
                    MessageBox.Show(owner, LocalizationManager.Text("UpdateLatestMessage"), LocalizationManager.Text("UpdateWindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return UpdateCheckOutcome.UpToDate;
            }

            status?.Invoke(LocalizationManager.Format("UpdateAvailableStatus", release.TagName));
            var updateWindow = new UpdateWindow(release, _releaseClient) { Owner = owner };
            updateWindow.ShowDialog();
            if (!updateWindow.InstallReady)
            {
                return UpdateCheckOutcome.UpdateAvailable;
            }

            LaunchUpdater(updateWindow.PackagePath!);
            status?.Invoke(LocalizationManager.Text("UpdateClosing"));
            if (owner != Application.Current.MainWindow)
            {
                owner.Close();
            }

            Application.Current.MainWindow?.Close();
            return UpdateCheckOutcome.InstallStarted;
        }
        catch (OperationCanceledException)
        {
            status?.Invoke(LocalizationManager.Text("UpdateCheckCancelled"));
            return UpdateCheckOutcome.Failed;
        }
        catch (Exception ex)
        {
            status?.Invoke(LocalizationManager.Text("UpdateCheckFailed"));
            if (showUpToDate)
            {
                MessageBox.Show(owner, ex.Message, LocalizationManager.Text("UpdateCheckFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            throw new FileNotFoundException(LocalizationManager.Text("UpdaterMissing"), updaterExecutable);
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
            "--pid", Environment.ProcessId.ToString(),
            "--language", Quote(LocalizationManager.CurrentLanguage));
        Process.Start(new ProcessStartInfo(temporaryUpdater, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = temporaryDirectory
        });
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
