using System.Windows;
using System.Windows.Controls;
using MeetingTransfer.App.Localization;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Core.Storage;

namespace MeetingTransfer.App;

public sealed class SessionOpenRequestedEventArgs(Guid sessionId) : EventArgs
{
    public Guid SessionId { get; } = sessionId;
}

public partial class SessionHistoryView : UserControl
{
    private bool _loaded;

    public SessionHistoryView(SqliteTranscriptStore store, Guid currentSessionId)
    {
        InitializeComponent();
        ViewModel = new SessionHistoryViewModel(store, currentSessionId);
        DataContext = ViewModel;
    }

    public SessionHistoryViewModel ViewModel { get; }
    public event EventHandler? CloseRequested;
    public event EventHandler? NewSessionRequested;
    public event EventHandler<SessionOpenRequestedEventArgs>? OpenSessionRequested;

    private async void SessionHistoryView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await ViewModel.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                ex.Message,
                LocalizationManager.Text("HistoryLoadFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void NewMeeting_Click(object sender, RoutedEventArgs e)
        => NewSessionRequested?.Invoke(this, EventArgs.Empty);

    private void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionHistoryItemViewModel item } && item.CanOpen)
        {
            OpenSessionRequested?.Invoke(this, new SessionOpenRequestedEventArgs(item.SessionId));
        }
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SessionHistoryItemViewModel item } || !item.CanDelete)
        {
            return;
        }

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            string.Format(LocalizationManager.Text("DeleteSessionConfirm"), item.Title),
            LocalizationManager.Text("DeleteSessionConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await ViewModel.DeleteAsync(item).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                ex.Message,
                LocalizationManager.Text("DeleteSessionFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
