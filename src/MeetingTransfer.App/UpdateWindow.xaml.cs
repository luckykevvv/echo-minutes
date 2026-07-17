using System.Windows;
using MeetingTransfer.App.Localization;
using MeetingTransfer.Core.Updates;

namespace MeetingTransfer.App;

public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly GitHubReleaseClient _releaseClient;
    private readonly CancellationTokenSource _cancellation = new();

    public UpdateWindow(ReleaseInfo release, GitHubReleaseClient releaseClient)
    {
        InitializeComponent();
        _release = release;
        _releaseClient = releaseClient;
        ReleaseTitleText.Text = release.DisplayName;
        VersionText.Text = LocalizationManager.Format("UpdateCurrentVersion", release.TagName, Updates.UpdateCoordinator.CurrentVersionText);
        ReleaseNotesText.Text = release.Notes;
        StatusText.Text = LocalizationManager.Format("UpdateVerificationHint", FormatSize(release.Package.Size));
    }

    public bool InstallReady { get; private set; }

    public string? PackagePath { get; private set; }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = LocalizationManager.Text("UpdateDownloading");
        var progress = new Progress<double>(value =>
        {
            DownloadProgress.Value = value;
            StatusText.Text = LocalizationManager.Format("UpdateDownloadingProgress", value);
        });

        try
        {
            PackagePath = await _releaseClient.DownloadAndVerifyAsync(_release, progress, _cancellation.Token);
            StatusText.Text = LocalizationManager.Text("UpdateVerifiedRestart");
            InstallReady = true;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationManager.Text("UpdateDownloadCancelled");
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.Text("UpdateDownloadFailed");
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            MessageBox.Show(this, ex.Message, LocalizationManager.Text("UpdateFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!InstallButton.IsEnabled)
        {
            _cancellation.Cancel();
            return;
        }

        DialogResult = false;
        Close();
    }

    private static string FormatSize(long bytes) => $"{bytes / 1024d / 1024d:N1} MiB";
}
