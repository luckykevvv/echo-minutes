using System.Windows;
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
        VersionText.Text = $"{release.TagName}  ·  current {Updates.UpdateCoordinator.CurrentVersionText}";
        ReleaseNotesText.Text = release.Notes;
        StatusText.Text = $"{FormatSize(release.Package.Size)} · SHA256 verified before installation";
    }

    public bool InstallReady { get; private set; }

    public string? PackagePath { get; private set; }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = "Downloading update…";
        var progress = new Progress<double>(value =>
        {
            DownloadProgress.Value = value;
            StatusText.Text = $"Downloading update… {value:P0}";
        });

        try
        {
            PackagePath = await _releaseClient.DownloadAndVerifyAsync(_release, progress, _cancellation.Token);
            StatusText.Text = "Download verified. EchoMinutes will restart.";
            InstallReady = true;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled.";
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update download failed.";
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
