using System.Collections.ObjectModel;
using MeetingTransfer.App.Localization;
using MeetingTransfer.Core.Storage;

namespace MeetingTransfer.App.ViewModels;

public sealed class SessionHistoryViewModel : ObservableObject
{
    private readonly SqliteTranscriptStore _store;
    private readonly Guid _currentSessionId;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public SessionHistoryViewModel(SqliteTranscriptStore store, Guid currentSessionId)
    {
        _store = store;
        _currentSessionId = currentSessionId;
        Sessions = [];
    }

    public ObservableCollection<SessionHistoryItemViewModel> Sessions { get; }
    public bool HasSessions => Sessions.Count > 0;
    public bool HasNoSessions => !HasSessions;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = LocalizationManager.CurrentLanguage == "zh-CN"
            ? "正在读取会话…"
            : "Loading sessions…";
        try
        {
            var sessions = await _store.ListSessionsAsync(cancellationToken).ConfigureAwait(true);
            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(new SessionHistoryItemViewModel(
                    session,
                    session.SessionId == _currentSessionId));
            }

            StatusMessage = Sessions.Count == 0
                ? string.Empty
                : LocalizationManager.CurrentLanguage == "zh-CN"
                    ? $"共 {Sessions.Count} 个会话"
                    : $"{Sessions.Count} session(s)";
            NotifyCollectionStateChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> DeleteAsync(
        SessionHistoryItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        if (item.IsCurrent || !await _store.DeleteAsync(item.SessionId, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        Sessions.Remove(item);
        StatusMessage = Sessions.Count == 0
            ? string.Empty
            : LocalizationManager.CurrentLanguage == "zh-CN"
                ? $"共 {Sessions.Count} 个会话"
                : $"{Sessions.Count} session(s)";
        NotifyCollectionStateChanged();
        return true;
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasNoSessions));
    }
}

public sealed class SessionHistoryItemViewModel
{
    public SessionHistoryItemViewModel(StoredSessionSummary session, bool isCurrent)
    {
        SessionId = session.SessionId;
        Title = session.Title;
        IsCurrent = isCurrent;
        CreatedAtDisplay = session.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm");
        var duration = session.Duration.TotalHours >= 1
            ? session.Duration.ToString(@"hh\:mm\:ss")
            : session.Duration.ToString(@"mm\:ss");
        Metadata = LocalizationManager.CurrentLanguage == "zh-CN"
            ? $"{session.SegmentCount} 段  ·  {session.SpeakerCount} 位说话人  ·  {duration}"
            : $"{session.SegmentCount} segments  ·  {session.SpeakerCount} speakers  ·  {duration}";
    }

    public Guid SessionId { get; }
    public string Title { get; }
    public string CreatedAtDisplay { get; }
    public string Metadata { get; }
    public bool IsCurrent { get; }
    public bool CanOpen => !IsCurrent;
    public bool CanDelete => !IsCurrent;
}
