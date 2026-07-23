using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Diagnostics;
using MeetingTransfer.App.Localization;
using MeetingTransfer.Audio;
using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Config;
using MeetingTransfer.Core.Export;
using MeetingTransfer.Core.Import;
using MeetingTransfer.Core.Models;
using MeetingTransfer.Core.Storage;
using MeetingTransfer.Core.Transcripts;
using MeetingTransfer.Stt;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly SettingsFileService _settingsFileService = new();
    private AppOptions _options;
    private SherpaOnnxOptions _sherpaOptions;
    private readonly MediaImportService _mediaImportService = new();
    private readonly ModelCatalog _modelCatalog = new();
    private readonly IAudioPlaybackService _audioPlaybackService;
    private SqliteTranscriptStore _store;
    private IAudioCaptureService? _audioCapture;
    private PcmSessionRecorder? _sessionRecorder;
    private ISpeechEngine? _speechEngine;
    private RealtimeTranscriptionPipeline? _pipeline;
    private CancellationTokenSource? _recordingCts;
    private CancellationTokenSource? _operationCts;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private AudioSource? _selectedSystemAudioSource;
    private AudioSource? _selectedMicrophoneSource;
    private string _statusMessage = "就绪。";
    private bool _captureSystemAudio = true;
    private bool _captureMicrophone = true;
    private bool _isChinese = true;
    private bool _isBusy;
    private bool _isRecording;
    private bool _isProgressIndeterminate;
    private bool _canCancelOperation;
    private double _operationProgress;
    private SpeakerCountOption? _selectedSpeakerCount;
    private DateTimeOffset _lastRealtimeErrorAt = DateTimeOffset.MinValue;
    private bool _isShuttingDown;
    private bool _hasDefaultDocumentTitle = true;
    private bool _isDocumentPersisted;
    private bool _isPlaybackActive;
    private bool _suppressNextPlaybackStoppedStatus;
    private string _segmentSearchText = string.Empty;

    public MainWindowViewModel(IAudioPlaybackService? audioPlaybackService = null)
    {
        _audioPlaybackService = audioPlaybackService ?? new AudioPlaybackService();
        _audioPlaybackService.PlaybackStopped += AudioPlaybackService_PlaybackStopped;
        (_options, _sherpaOptions) = LoadOptions();
        _isChinese = LocalizationManager.Normalize(_options.Ui.Language) == "zh-CN";
        LocalizationManager.LanguageChanged += LocalizationManager_LanguageChanged;
        _store = new SqliteTranscriptStore(_options.Storage.DatabasePath);

        Document = new TranscriptDocument
        {
            Title = Text(
                $"会议 {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
                $"Meeting {DateTimeOffset.Now:yyyy-MM-dd HH:mm}")
        };
        Segments = new ObservableCollection<TranscriptSegment>(Document.Segments);
        SegmentsView = CollectionViewSource.GetDefaultView(Segments);
        SegmentsView.Filter = FilterSegment;
        Speakers = new ObservableCollection<Speaker>(Document.Speakers);
        SystemAudioSources = [];
        MicrophoneSources = [];
        SpeakerCountOptions = [];
        RebuildSpeakerCountOptions(-1);

        StartCommand = new RelayCommand(StartAsync, () => !IsBusy && !IsRecording && !_isShuttingDown);
        StopCommand = new RelayCommand(StopAsync, () => IsRecording && !_isShuttingDown);
        ImportCommand = new RelayCommand(ImportAsync, () => !IsBusy && !IsRecording && !_isShuttingDown);
        CancelOperationCommand = new RelayCommand(
            CancelOperationAsync,
            () => CanCancelOperation && !_isShuttingDown);
        ExportCommand = new RelayCommand(ExportAsync, () => !IsBusy && !IsRecording && !_isShuttingDown);
        HistoryCommand = new RelayCommand(OpenHistoryAsync, () => !IsBusy && !IsRecording && !_isShuttingDown);
        SettingsCommand = new RelayCommand(OpenSettingsAsync, () => !IsBusy && !IsRecording && !_isShuttingDown);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguageAsync, onError: HandleCommandError);
        MergeSpeakerCommand = new RelayCommand(
            MergeSpeakerAsync,
            speaker => speaker is Speaker source &&
                Speakers.Count > 1 &&
                !IsBusy &&
                !IsRecording &&
                !string.Equals(Speakers[0].Id, source.Id, StringComparison.Ordinal));
        DeleteSegmentCommand = new RelayCommand(
            DeleteSegmentAsync,
            segment => segment is TranscriptSegment && !IsBusy && !IsRecording);
        MergePreviousSegmentCommand = new RelayCommand(
            MergePreviousSegmentAsync,
            segment => segment is TranscriptSegment item && CanMergeWithPrevious(item) && !IsBusy && !IsRecording);
        PlaySegmentCommand = new RelayCommand(
            PlaySegmentAsync,
            segment => segment is TranscriptSegment && !IsBusy && !IsRecording && !_isShuttingDown);
        StopPlaybackCommand = new RelayCommand(
            StopPlaybackAsync,
            () => IsPlaybackActive && !_isShuttingDown);

        UpdateModelReadinessStatus();
        LoadAudioSources();
    }

    public TranscriptDocument Document { get; private set; }
    public ObservableCollection<TranscriptSegment> Segments { get; }
    public ICollectionView SegmentsView { get; }
    public ObservableCollection<Speaker> Speakers { get; }
    public ObservableCollection<AudioSource> SystemAudioSources { get; }
    public ObservableCollection<AudioSource> MicrophoneSources { get; }
    public ObservableCollection<SpeakerCountOption> SpeakerCountOptions { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand HistoryCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand MergeSpeakerCommand { get; }
    public RelayCommand DeleteSegmentCommand { get; }
    public RelayCommand MergePreviousSegmentCommand { get; }
    public RelayCommand PlaySegmentCommand { get; }
    public RelayCommand StopPlaybackCommand { get; }
    public event EventHandler? SettingsRequested;
    public event EventHandler? HistoryRequested;
    public bool ShouldShowOnboarding => !_options.Ui.OnboardingCompleted;
    public SqliteTranscriptStore SessionStore => _store;

    public Task CompleteOnboardingAsync()
    {
        try
        {
            var settings = _settingsFileService.Load();
            settings.App.Ui.OnboardingCompleted = true;
            _settingsFileService.Save(settings);
            (_options, _sherpaOptions) = LoadOptions();
            _store = new SqliteTranscriptStore(_options.Storage.DatabasePath);
            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(StoragePath));
            UpdateModelReadinessStatus();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{Text("无法保存新手引导状态", "Could not save onboarding state")}: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    public string AppTitle => "EchoMinutes";
    public string EngineStatus
    {
        get
        {
            var model = _modelCatalog.FindById(_sherpaOptions.ActiveModelId ?? "");
            if (model is null || !_modelCatalog.IsInstalled(model))
            {
                return Text("导入引擎: 未安装", "Import engine: not installed");
            }

            var engine = model.EngineDisplay;
            var backend = model.Backend;
            return $"{Text("导入引擎", "Import engine")}: {engine} · {backend}";
        }
    }
    public string StoragePath => $"{Text("数据库", "DB")}: {_options.Storage.DatabasePath}";
    public string SessionSummary => Text(
        $"{Segments.Count} 段，{Speakers.Count} 位说话人，{Document.AudioTracks.Count} 条录音",
        $"{Segments.Count} segments, {Speakers.Count} speakers, {Document.AudioTracks.Count} recording(s)");
    public string ToggleLanguageLabel => _isChinese ? "EN" : "中文";
    public string WorkbenchLabel => Text("实时工作台", "Live Workbench");
    public string DevicesLabel => Text("输入设备", "Input Devices");
    public string SystemAudioLabel => Text("系统音频", "System Audio");
    public string CaptureSystemAudioLabel => Text("捕获系统音频", "Capture system audio");
    public string MicrophoneLabel => Text("麦克风", "Microphone");
    public string CaptureMicrophoneLabel => Text("捕获麦克风", "Capture microphone");
    public string StartLabel => Text("开始", "Start");
    public string StopLabel => Text("停止", "Stop");
    public string ImportLabel => Text("导入", "Import");
    public string CancelOperationLabel => Text("取消处理", "Cancel task");
    public string ExpectedSpeakersLabel => Text("本次说话人数", "Speakers for this import");
    public string ExportLabel => Text("导出", "Export");
    public string HistoryLabel => Text("历史会话", "Session history");
    public string SettingsLabel => Text("设置", "Settings");
    public string TranscriptLabel => Text("转写记录", "Transcript");
    public string SearchTranscriptLabel => Text("搜索转写", "Search transcript");
    public string SegmentEditHint => Text("直接编辑文本；Ctrl+Enter 在光标处拆分", "Edit text directly; Ctrl+Enter splits at the caret");
    public string MergePreviousLabel => Text("合并上一段", "Merge previous");
    public string DeleteSegmentLabel => Text("删除片段", "Delete segment");
    public string PlaySegmentActionLabel => Text("播放", "Play");
    public string PlaySegmentLabel => Text("从此处播放录音", "Play recording from here");
    public string StopPlaybackLabel => Text("停止播放", "Stop playback");
    public string SpeakersLabel => Text("说话人", "Speakers");
    public string SpeakersHint => Text("直接编辑标签；Enter 保存，Esc 取消", "Edit labels inline; Enter saves, Esc cancels");
    public string NoSpeakersTitle => Text("尚未识别到说话人", "No speakers yet");
    public string NoSpeakersHint => Text("产生转写后，可在这里直接修改每位说话人的标签。", "Speaker labels can be edited here after transcription begins.");
    public string MergeSpeakerLabel => Text("合并到首位", "Merge into first");
    public string SessionLabel => Text("会话", "Session");
    public bool HasSpeakers => Speakers.Count > 0;
    public bool HasNoSpeakers => !HasSpeakers;
    public bool IsPlaybackActive
    {
        get => _isPlaybackActive;
        private set
        {
            if (SetProperty(ref _isPlaybackActive, value))
            {
                StopPlaybackCommand?.RaiseCanExecuteChanged();
            }
        }
    }
    public string ProgressLabel => IsRecording
        ? Text("实时转写中", "Recording live")
        : IsBusy
            ? Text("处理中", "Working")
            : Text("空闲", "Idle");

    public string SegmentSearchText
    {
        get => _segmentSearchText;
        set
        {
            if (SetProperty(ref _segmentSearchText, value))
            {
                SegmentsView.Refresh();
            }
        }
    }

    public bool CaptureSystemAudio
    {
        get => _captureSystemAudio;
        set => SetProperty(ref _captureSystemAudio, value);
    }

    public bool CaptureMicrophone
    {
        get => _captureMicrophone;
        set => SetProperty(ref _captureMicrophone, value);
    }

    public AudioSource? SelectedSystemAudioSource
    {
        get => _selectedSystemAudioSource;
        set => SetProperty(ref _selectedSystemAudioSource, value);
    }

    public AudioSource? SelectedMicrophoneSource
    {
        get => _selectedMicrophoneSource;
        set => SetProperty(ref _selectedMicrophoneSource, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
                RaiseOperationCanExecuteChanged();
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
                RaiseOperationCanExecuteChanged();
            }
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double OperationProgress
    {
        get => _operationProgress;
        set => SetProperty(ref _operationProgress, value);
    }

    public bool CanCancelOperation
    {
        get => _canCancelOperation;
        private set
        {
            if (SetProperty(ref _canCancelOperation, value))
            {
                CancelOperationCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public SpeakerCountOption? SelectedSpeakerCount
    {
        get => _selectedSpeakerCount;
        set => SetProperty(ref _selectedSpeakerCount, value);
    }

    private void LoadAudioSources()
    {
        try
        {
            var capture = new WasapiAudioCaptureService();
            var sources = capture.GetAvailableSources();
            foreach (var source in sources.Where(x => x.Kind == AudioSourceKind.SystemAudio))
            {
                SystemAudioSources.Add(source);
            }

            foreach (var source in sources.Where(x => x.Kind == AudioSourceKind.Microphone))
            {
                MicrophoneSources.Add(source);
            }

            SelectedSystemAudioSource = SystemAudioSources.FirstOrDefault();
            SelectedMicrophoneSource = MicrophoneSources.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{Text("音频设备发现失败", "Audio device discovery failed")}: {ex.Message}";
        }
    }

    private async Task StartAsync()
    {
        if (!HasRealtimeModel())
        {
            var openSettings = MessageBox.Show(
                Application.Current.MainWindow,
                Text(
                    "实时转写需要先下载 Realtime Paraformer。是否现在打开设置？",
                    "Realtime transcription requires Realtime Paraformer. Open Settings now?"),
                Text("缺少实时模型", "Realtime model required"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            StatusMessage = Text("缺少 Realtime Paraformer。", "Realtime Paraformer is not installed.");
            if (openSettings == MessageBoxResult.Yes)
            {
                await OpenSettingsAsync().ConfigureAwait(true);
            }
            return;
        }

        try
        {
            StopPlayback();
            await StopInternalAsync(false).ConfigureAwait(true);
            // StopInternal resets progress state. Mark this operation busy only
            // after cleanup so import/settings/history stay disabled while the
            // model and audio devices are still being initialized.
            BeginProgress(Text("正在初始化实时转写...", "Initializing live transcription..."), true, 10);
            _recordingCts = new CancellationTokenSource();
            _speechEngine = CreateSpeechEngine();
            SetProgress(Text("正在加载实时模型...", "Loading realtime model..."), true, 35);
            await _speechEngine.InitializeAsync(_recordingCts.Token).ConfigureAwait(true);

            _pipeline = new RealtimeTranscriptionPipeline(
                _speechEngine,
                Document,
                capacity: 32,
                processingCancellationToken: _recordingCts.Token);
            _pipeline.SegmentFinalized += (_, segment) => Application.Current.Dispatcher.Invoke(() => AddSegment(segment));
            _pipeline.ProcessingFailed += ReportRealtimeFailure;

            _audioCapture = new WasapiAudioCaptureService();
            _audioCapture.ChunkReady += (_, chunk) =>
            {
                _sessionRecorder?.Write(chunk);
                var pipeline = _pipeline;
                if (pipeline is null || !pipeline.TryEnqueue(chunk))
                {
                    ReportRealtimeFailure(new InvalidOperationException(Text(
                        "实时识别跟不上音频输入，已跳过一个新的转写块。录音文件仍然完整。",
                        "Live recognition is behind the audio input; one new transcription chunk was skipped. The recording remains complete.")));
                }
            };

            var timelineOffset = GetNextTimelineOffset();
            var recordingDirectory = Path.Combine(
                _options.Storage.RecordingsDirectory,
                Document.SessionId.ToString("N"));
            _sessionRecorder = new PcmSessionRecorder(recordingDirectory);

            SetProgress(Text("正在打开音频设备...", "Opening audio devices..."), true, 65);
            await _audioCapture.StartAsync(new AudioCaptureRequest(
                CaptureSystemAudio,
                CaptureMicrophone,
                SelectedSystemAudioSource?.Id,
                SelectedMicrophoneSource?.Id,
                _options.Audio.SampleRate,
                _options.Audio.Channels,
                _options.Audio.ChunkMilliseconds,
                timelineOffset), _recordingCts.Token).ConfigureAwait(true);

            IsBusy = false;
            IsRecording = true;
            IsProgressIndeterminate = true;
            OperationProgress = 100;
            StatusMessage = Text("正在录音并实时转写。", "Recording and transcribing live.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Realtime capture failed to start.", ex);
            var failure = $"{Text("启动失败", "Start failed")}: {ex.Message}";
            try
            {
                await StopInternalAsync(false).ConfigureAwait(true);
            }
            catch (Exception cleanupError)
            {
                failure += $" ({Text("清理失败", "cleanup failed")}: {cleanupError.Message})";
            }
            EndProgress(failure);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await StopInternalAsync(true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Realtime capture failed to stop cleanly.", ex);
            EndProgress($"{Text("停止失败", "Stop failed")}: {ex.Message}");
        }
    }

    private async Task StopInternalAsync(bool updateStatus)
    {
        await _stopGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (updateStatus)
            {
                BeginProgress(Text("正在停止并保存...", "Stopping and saving..."), true, 25);
            }

            IReadOnlyList<SessionAudioTrack> recordedTracks = [];
            var recorder = _sessionRecorder;
            _sessionRecorder = null;
            _recordingCts?.Cancel();

            if (_audioCapture is not null)
            {
                await _audioCapture.StopAsync(CancellationToken.None).ConfigureAwait(true);
                await _audioCapture.DisposeAsync().ConfigureAwait(true);
                _audioCapture = null;
            }

            if (recorder is not null)
            {
                recorder.Dispose();
                recordedTracks = recorder.RecordedTracks;
            }

            if (_pipeline is not null)
            {
                _pipeline.ProcessingFailed -= ReportRealtimeFailure;
                await _pipeline.FinalizeAsync(CancellationToken.None).ConfigureAwait(true);
                _pipeline = null;
            }

            if (_speechEngine is not null)
            {
                await _speechEngine.DisposeAsync().ConfigureAwait(true);
                _speechEngine = null;
            }

            _recordingCts?.Dispose();
            _recordingCts = null;
            foreach (var track in recordedTracks)
            {
                if (Document.AudioTracks.All(existing => existing.Id != track.Id))
                {
                    Document.AudioTracks.Add(track);
                }
            }
            OnPropertyChanged(nameof(SessionSummary));

            if (_isDocumentPersisted ||
                Document.Segments.Count > 0 ||
                Document.Speakers.Count > 0 ||
                Document.AudioTracks.Count > 0)
            {
                await _store.SaveAsync(Document).ConfigureAwait(true);
                _isDocumentPersisted = true;
                DiagnosticLog.Info($"Saved session {Document.SessionId:N} with {Document.Segments.Count} segment(s).");
            }
            else
            {
                DiagnosticLog.Info($"Skipped new empty session {Document.SessionId:N}.");
            }
            IsRecording = false;
            if (updateStatus &&
                _options.PostProcessing.RefineRecordingOnStop &&
                HasOfflineModel() &&
                recordedTracks.Count > 0)
            {
                try
                {
                    _operationCts?.Dispose();
                    _operationCts = new CancellationTokenSource();
                    CanCancelOperation = true;
                    SetProgress(Text(
                        "实时稿已保存，正在生成高质量最终稿…",
                        "Live draft saved; creating the high-quality final transcript…"), false, 5);
                    await RefineRecordedSessionAsync(recordedTracks, _operationCts.Token).ConfigureAwait(true);
                    EndProgress(Text(
                        "已停止并生成高质量最终稿。",
                        "Stopped and created the high-quality final transcript."), completed: true);
                }
                catch (OperationCanceledException)
                {
                    DiagnosticLog.Info($"Offline refinement cancelled for session {Document.SessionId:N}.");
                    EndProgress(Text(
                        "高质量精修已取消；实时稿和录音已保存。",
                        "High-quality refinement cancelled; the live draft and recording were saved."));
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"Offline refinement failed for session {Document.SessionId:N}.", ex);
                    EndProgress($"{Text(
                        "实时稿和录音已保存，但高质量精修失败",
                        "The live draft and recording were saved, but refinement failed")}: {ex.Message}");
                }
                finally
                {
                    CanCancelOperation = false;
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }
            else
            {
                EndProgress(updateStatus
                    ? Text("已停止并保存。", "Stopped and saved.")
                    : Text("就绪。", "Ready."));
            }
        }
        finally
        {
            _stopGate.Release();
        }
    }

    private async Task RefineRecordedSessionAsync(
        IReadOnlyList<SessionAudioTrack> tracks,
        CancellationToken cancellationToken)
    {
        var usableTracks = tracks
            .Where(track => File.Exists(track.Path) && new FileInfo(track.Path).Length > 44)
            .ToArray();
        if (usableTracks.Length == 0)
        {
            return;
        }

        await using var engine = CreateSpeechEngine();
        await engine.InitializeAsync(cancellationToken).ConfigureAwait(true);
        var refinedSegments = new List<TranscriptSegment>();
        for (var index = 0; index < usableTracks.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = usableTracks[index];
            var trackStart = index / (double)usableTracks.Length * 90;
            var trackShare = 90d / usableTracks.Length;
            var progress = new Progress<TranscriptionProgress>(item =>
            {
                var mapped = Math.Clamp(MapImportProgress(item), 0, 100);
                SetProgress(
                    Text(
                        $"正在精修音轨 {index + 1}/{usableTracks.Length}…",
                        $"Refining track {index + 1}/{usableTracks.Length}…"),
                    false,
                    Math.Min(95, 5 + trackStart + mapped / 100d * trackShare));
            });
            var segments = await engine.TranscribeFileAsync(
                track.Path,
                track.SourceId,
                progress,
                cancellationToken).ConfigureAwait(true);
            foreach (var segment in segments)
            {
                var isMicrophone = track.SourceKind == AudioSourceKind.Microphone;
                refinedSegments.Add(new TranscriptSegment
                {
                    SpeakerId = isMicrophone ? "local-user" : segment.SpeakerId,
                    SpeakerName = isMicrophone ? Text("我", "Me") : LocalizeDefaultSpeakerName(segment.SpeakerName),
                    SourceId = track.SourceId,
                    SourceKind = track.SourceKind,
                    Start = track.TimelineOffset + segment.Start,
                    End = track.TimelineOffset + segment.End,
                    Text = segment.Text,
                    Confidence = segment.Confidence,
                    IsProvisional = false
                });
            }
        }

        if (refinedSegments.Count == 0)
        {
            throw new InvalidOperationException(Text(
                "离线模型没有返回可用文本。",
                "The offline model returned no usable transcript."));
        }

        var refinedDocument = new TranscriptDocument
        {
            SessionId = Document.SessionId,
            Title = Document.Title,
            CreatedAt = Document.CreatedAt
        };
        refinedDocument.AudioTracks.AddRange(Document.AudioTracks);
        foreach (var segment in refinedSegments.OrderBy(item => item.Start))
        {
            refinedDocument.EnsureSpeaker(
                segment.SpeakerId,
                segment.SpeakerName,
                segment.SourceKind == AudioSourceKind.Microphone);
            refinedDocument.Segments.Add(segment);
        }

        SetProgress(Text("正在保存高质量最终稿…", "Saving the high-quality final transcript…"), false, 98);
        // Keep the live draft visible and in memory until the refined document
        // has been committed successfully. A cancellation or storage failure
        // therefore cannot leave the UI showing an unsaved half-transition.
        await _store.SaveAsync(refinedDocument, cancellationToken).ConfigureAwait(true);
        ReplaceDocument(refinedDocument, hasDefaultTitle: _hasDefaultDocumentTitle, isPersisted: true);
    }

    private async Task ImportAsync()
    {
        if (!HasOfflineModel())
        {
            var openSettings = MessageBox.Show(
                Application.Current.MainWindow,
                Text(
                    "离线导入需要先下载一个 Offline 模型并设为默认。是否现在打开设置？",
                    "Offline import requires a downloaded Offline model set as default. Open Settings now?"),
                Text("缺少离线模型", "Offline model required"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            StatusMessage = Text("未安装或未选择离线模型。", "No offline model is installed and selected.");
            if (openSettings == MessageBoxResult.Yes)
            {
                await OpenSettingsAsync().ConfigureAwait(true);
            }
            return;
        }

        if (!HasDiarizationModel())
        {
            var continueWithoutSpeakers = MessageBox.Show(
                Application.Current.MainWindow,
                Text(
                    "尚未下载 Speaker diarization。选择“是”继续仅转写（不会自动分人）；选择“否”打开设置下载；取消则返回。",
                    "Speaker diarization is not installed. Choose Yes to transcribe without speaker labels, No to open Settings, or Cancel to return."),
                Text("缺少说话人分离模型", "Speaker diarization not installed"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information,
                MessageBoxResult.No);
            if (continueWithoutSpeakers == MessageBoxResult.No)
            {
                await OpenSettingsAsync().ConfigureAwait(true);
                return;
            }
            if (continueWithoutSpeakers != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var dialog = new OpenFileDialog
        {
            Filter = $"{Text("媒体文件", "Media files")}|*.wav;*.mp3;*.m4a;*.mp4;*.mkv;*.mov|" +
                     $"{Text("所有文件", "All files")}|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DiagnosticLog.Info("Media import started.");
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            CanCancelOperation = true;
            var cancellationToken = _operationCts.Token;
            BeginProgress(Text("正在抽取音频...", "Extracting audio..."), true, 20);
            var wavPath = await _mediaImportService.ExtractAudioAsync(
                dialog.FileName,
                _options.Import.FfmpegPath,
                _options.Storage.RecordingsDirectory,
                cancellationToken).ConfigureAwait(true);

            SetProgress(Text("正在加载识别模型...", "Loading speech model..."), true, 45);
            // Speaker count is intentionally a per-import choice. Auto (-1) uses
            // threshold clustering; a known count asks sherpa to produce exactly N.
            _sherpaOptions.DiarizationClusterCount = SelectedSpeakerCount?.Value ?? -1;
            await using var engine = CreateSpeechEngine();
            await engine.InitializeAsync(cancellationToken).ConfigureAwait(true);
            // ASR owns 45-90% of the combined bar. Start at its real lower bound;
            // raw decoder callbacks will advance it instead of jumping to a fake 70%.
            SetProgress(Text("正在转写文件...", "Transcribing file..."), false, 45);

            // Stream transcription progress to the side panel so the user can see
            // chunk-by-chunk progress (e.g. "60% decoding... 12 segment(s) so far")
            // instead of a single stuck-at-70% bar. Progress<T> auto-marshals to the UI
            // thread when we capture SynchronizationContext, which WPF supplies.
            var progress = new Progress<TranscriptionProgress>(p =>
            {
                var msg = FormatTranscriptionProgress(p);
                var overallPercent = MapImportProgress(p);
                SetProgress(msg, false, Math.Max(OperationProgress, overallPercent));
            });
            var segments = await engine.TranscribeFileAsync(
                    wavPath,
                    Path.GetFileName(dialog.FileName),
                    progress,
                    cancellationToken)
                .ConfigureAwait(true);

            // An imported recording is a new transcript session. Keeping the old
            // document here caused speaker ids such as speaker-1 to collide across
            // separate files and made unrelated people appear to be the same speaker.
            Document = new TranscriptDocument
            {
                Title = Path.GetFileNameWithoutExtension(dialog.FileName),
            };
            _hasDefaultDocumentTitle = false;
            _isDocumentPersisted = false;
            Document.AudioTracks.Add(new SessionAudioTrack(
                Guid.NewGuid(),
                wavPath,
                Path.GetFileName(dialog.FileName),
                AudioSourceKind.ImportedFile,
                TimeSpan.Zero,
                segments.Count == 0 ? null : segments.Max(segment => segment.End)));
            RefreshCollections();
            foreach (var segment in segments)
            {
                AddSegment(segment);
            }

            SetProgress(Text("正在保存会话...", "Saving session..."), false, 95);
            await _store.SaveAsync(Document).ConfigureAwait(true);
            _isDocumentPersisted = true;
            DiagnosticLog.Info($"Media import completed for session {Document.SessionId:N} with {Document.Segments.Count} segment(s).");
            EndProgress(Text($"已导入 {Path.GetFileName(dialog.FileName)}。", $"Imported {Path.GetFileName(dialog.FileName)}."), completed: true);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Info("Media import cancelled.");
            EndProgress(Text("导入已取消。", "Import cancelled."));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Media import failed.", ex);
            EndProgress($"{Text("导入失败", "Import failed")}: {ex.Message}");
        }
        finally
        {
            CanCancelOperation = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private void ReportRealtimeFailure(Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRealtimeErrorAt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastRealtimeErrorAt = now;
        DiagnosticLog.Error("Realtime transcription failure.", exception);
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
            StatusMessage = $"{Text("实时识别暂时失败", "Realtime recognition temporarily failed")}: {exception.Message}");
    }

    private Task CancelOperationAsync()
    {
        if (_operationCts is null || _operationCts.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        StatusMessage = Text("正在取消当前任务…", "Cancelling the current task…");
        _operationCts.Cancel();
        CanCancelOperation = false;
        return Task.CompletedTask;
    }

    private async Task ExportAsync()
    {
        try
        {
            BeginProgress(Text("正在导出...", "Exporting..."), false, 45);
            Directory.CreateDirectory(_options.Storage.ExportsDirectory);
            var safeTitle = SanitizeFileName(Document.Title);
            var basePath = Path.Combine(_options.Storage.ExportsDirectory, string.IsNullOrWhiteSpace(safeTitle) ? "meeting" : safeTitle);

            await File.WriteAllTextAsync(basePath + ".txt", TranscriptExporter.Export(Document, TranscriptExportFormat.Text));
            await File.WriteAllTextAsync(basePath + ".md", TranscriptExporter.Export(Document, TranscriptExportFormat.Markdown));
            await File.WriteAllTextAsync(basePath + ".srt", TranscriptExporter.Export(Document, TranscriptExportFormat.Srt));
            await File.WriteAllTextAsync(basePath + ".vtt", TranscriptExporter.Export(Document, TranscriptExportFormat.Vtt));
            await File.WriteAllTextAsync(basePath + ".json", TranscriptExporter.Export(Document, TranscriptExportFormat.Json));

            EndProgress(Text($"已导出到 {_options.Storage.ExportsDirectory}。", $"Exported to {_options.Storage.ExportsDirectory}."), completed: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Transcript export failed.", ex);
            EndProgress($"{Text("导出失败", "Export failed")}: {ex.Message}");
        }
    }

    private Task PlaySegmentAsync(object? parameter)
    {
        if (parameter is not TranscriptSegment segment)
        {
            return Task.CompletedTask;
        }

        var track = SessionAudioTrackResolver.Resolve(Document.AudioTracks, segment);
        if (track is null)
        {
            StopPlayback();
            StatusMessage = Text(
                "这个片段没有可关联的录音轨道；旧会话可能只保存了转写。",
                "This segment has no linked recording; older sessions may contain transcript data only.");
            return Task.CompletedTask;
        }

        if (!File.Exists(track.Path))
        {
            StopPlayback();
            DiagnosticLog.Info($"Missing recording for session {Document.SessionId:N}: {track.Path}");
            StatusMessage = Text(
                $"找不到录音文件：{Path.GetFileName(track.Path)}。文件可能已被移动或删除。",
                $"Recording not found: {Path.GetFileName(track.Path)}. It may have been moved or deleted.");
            return Task.CompletedTask;
        }

        try
        {
            StopPlayback();
            _suppressNextPlaybackStoppedStatus = false;
            var position = SessionAudioTrackResolver.GetSeekPosition(track, segment);
            _audioPlaybackService.Play(track.Path, position);
            IsPlaybackActive = true;
            StatusMessage = Text(
                $"正在播放 {Path.GetFileName(track.Path)} · {position:mm\\:ss}",
                $"Playing {Path.GetFileName(track.Path)} · {position:mm\\:ss}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Recording playback failed.", ex);
            IsPlaybackActive = false;
            StatusMessage = $"{Text("录音播放失败", "Recording playback failed")}: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private Task StopPlaybackAsync()
    {
        StopPlayback(updateStatus: true);
        return Task.CompletedTask;
    }

    private void StopPlayback(bool updateStatus = false)
    {
        if (!IsPlaybackActive && !_audioPlaybackService.IsPlaying)
        {
            return;
        }

        _suppressNextPlaybackStoppedStatus = !updateStatus;
        IsPlaybackActive = false;
        try
        {
            _audioPlaybackService.Stop();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Could not stop recording playback cleanly.", ex);
        }

        if (updateStatus)
        {
            StatusMessage = Text("已停止播放。", "Playback stopped.");
        }
    }

    private void AudioPlaybackService_PlaybackStopped(object? sender, AudioPlaybackStoppedEventArgs e)
    {
        var suppressStatus = _suppressNextPlaybackStoppedStatus;
        _suppressNextPlaybackStoppedStatus = false;

        void ApplyStoppedState()
        {
            IsPlaybackActive = false;
            if (e.Exception is not null)
            {
                DiagnosticLog.Error("Recording playback stopped with an error.", e.Exception);
                StatusMessage = $"{Text("录音播放失败", "Recording playback failed")}: {e.Exception.Message}";
            }
            else if (!suppressStatus)
            {
                StatusMessage = Text("录音播放结束。", "Recording playback finished.");
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(ApplyStoppedState);
        }
        else
        {
            ApplyStoppedState();
        }
    }

    public bool CommitSpeakerName(string speakerId, string name)
    {
        var speaker = Document.Speakers.FirstOrDefault(item =>
            string.Equals(item.Id, speakerId, StringComparison.Ordinal));
        var normalized = name.Trim();
        if (speaker is null || string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (string.Equals(speaker.Name, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        Document.RenameSpeaker(speaker.Id, normalized);
        RefreshSegments();
        return true;
    }

    public bool CommitSegmentText(Guid segmentId, string text)
    {
        var segment = Document.Segments.FirstOrDefault(item => item.Id == segmentId);
        var normalized = text.Trim();
        if (segment is null || string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        segment.Text = normalized;
        SegmentsView.Refresh();
        return true;
    }

    public bool SplitSegment(Guid segmentId, int characterIndex)
    {
        StopPlayback();
        var segment = Document.Segments.FirstOrDefault(item => item.Id == segmentId);
        if (segment is null || characterIndex <= 0 || characterIndex >= segment.Text.Length)
        {
            return false;
        }

        var leftText = segment.Text[..characterIndex].Trim();
        var rightText = segment.Text[characterIndex..].Trim();
        if (leftText.Length == 0 || rightText.Length == 0)
        {
            return false;
        }

        var ratio = characterIndex / (double)segment.Text.Length;
        var splitTime = segment.Start + TimeSpan.FromTicks((long)((segment.End - segment.Start).Ticks * ratio));
        var left = CloneSegment(segment, segment.Start, splitTime, leftText);
        var right = CloneSegment(segment, splitTime, segment.End, rightText);
        var index = Document.Segments.IndexOf(segment);
        Document.Segments.RemoveAt(index);
        Document.Segments.Insert(index, right);
        Document.Segments.Insert(index, left);
        RefreshSegments();
        return true;
    }

    private Task DeleteSegmentAsync(object? parameter)
    {
        StopPlayback();
        if (parameter is not TranscriptSegment segment || !Document.Segments.Remove(segment))
        {
            return Task.CompletedTask;
        }

        RemoveUnusedSpeakers();
        RefreshCollections();
        return Task.CompletedTask;
    }

    private Task MergePreviousSegmentAsync(object? parameter)
    {
        StopPlayback();
        if (parameter is not TranscriptSegment segment)
        {
            return Task.CompletedTask;
        }

        var ordered = Document.Segments.OrderBy(item => item.Start).ToList();
        var index = ordered.IndexOf(segment);
        if (index <= 0)
        {
            return Task.CompletedTask;
        }

        var previous = ordered[index - 1];
        if (!string.Equals(previous.SpeakerId, segment.SpeakerId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var merged = CloneSegment(
            previous,
            previous.Start,
            TimeSpan.FromTicks(Math.Max(previous.End.Ticks, segment.End.Ticks)),
            $"{previous.Text.Trim()} {segment.Text.Trim()}".Trim());
        Document.Segments.Remove(previous);
        Document.Segments.Remove(segment);
        Document.Segments.Add(merged);
        RefreshSegments();
        return Task.CompletedTask;
    }

    private bool CanMergeWithPrevious(TranscriptSegment segment)
    {
        var ordered = Document.Segments.OrderBy(item => item.Start).ToList();
        var index = ordered.IndexOf(segment);
        return index > 0 && string.Equals(ordered[index - 1].SpeakerId, segment.SpeakerId, StringComparison.Ordinal);
    }

    private static TranscriptSegment CloneSegment(
        TranscriptSegment source,
        TimeSpan start,
        TimeSpan end,
        string text)
        => new()
        {
            SpeakerId = source.SpeakerId,
            SpeakerName = source.SpeakerName,
            SourceId = source.SourceId,
            SourceKind = source.SourceKind,
            Start = start,
            End = end,
            Text = text,
            Confidence = source.Confidence,
            IsProvisional = source.IsProvisional
        };

    private void RemoveUnusedSpeakers()
        => Document.Speakers.RemoveAll(speaker =>
            Document.Segments.All(segment => !string.Equals(segment.SpeakerId, speaker.Id, StringComparison.Ordinal)));

    private Task MergeSpeakerAsync(object? parameter)
    {
        if (parameter is not Speaker source || Speakers.Count < 2)
        {
            return Task.CompletedTask;
        }

        var target = Speakers[0];
        if (string.Equals(source.Id, target.Id, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Document.MergeSpeakers(source.Id, target.Id);
        RefreshCollections();
        return Task.CompletedTask;
    }

    private void AddSegment(TranscriptSegment segment)
    {
        segment.SpeakerName = LocalizeDefaultSpeakerName(segment.SpeakerName);
        Document.EnsureSpeaker(segment.SpeakerId, segment.SpeakerName, segment.SourceKind == AudioSourceKind.Microphone);
        if (!Document.Segments.Contains(segment))
        {
            Document.Segments.Add(segment);
        }

        RefreshCollections();
    }

    private void RefreshCollections()
    {
        RefreshSegments();

        Speakers.Clear();
        foreach (var speaker in Document.Speakers)
        {
            Speakers.Add(speaker);
        }

        OnPropertyChanged(nameof(HasSpeakers));
        OnPropertyChanged(nameof(HasNoSpeakers));
        MergeSpeakerCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSegments()
    {
        Segments.Clear();
        foreach (var segment in Document.Segments.OrderBy(x => x.Start))
        {
            Segments.Add(segment);
        }

        OnPropertyChanged(nameof(SessionSummary));
        SegmentsView.Refresh();
        DeleteSegmentCommand?.RaiseCanExecuteChanged();
        MergePreviousSegmentCommand?.RaiseCanExecuteChanged();
    }

    private bool FilterSegment(object item)
    {
        if (item is not TranscriptSegment segment || string.IsNullOrWhiteSpace(SegmentSearchText))
        {
            return true;
        }

        return segment.Text.Contains(SegmentSearchText, StringComparison.CurrentCultureIgnoreCase) ||
               segment.SpeakerName.Contains(SegmentSearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private ISpeechEngine CreateSpeechEngine()
        => new SherpaOnnxSpeechEngine(_sherpaOptions);

    private static string SanitizeFileName(string value)
        => string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private Task OpenSettingsAsync()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private Task OpenHistoryAsync()
    {
        HistoryRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task NewSessionAsync(CancellationToken cancellationToken = default)
    {
        await PersistCurrentSessionIfNeededAsync(cancellationToken).ConfigureAwait(true);
        ReplaceDocument(CreateDefaultDocument(), hasDefaultTitle: true, isPersisted: false);
        EndProgress(Text("已新建会议。", "New meeting created."));
    }

    public async Task OpenStoredSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Document.SessionId)
        {
            return;
        }

        await PersistCurrentSessionIfNeededAsync(cancellationToken).ConfigureAwait(true);
        var loaded = await _store.LoadAsync(sessionId, cancellationToken).ConfigureAwait(true)
            ?? throw new InvalidOperationException(Text("找不到所选会话。", "The selected session no longer exists."));
        ReplaceDocument(loaded, hasDefaultTitle: false, isPersisted: true);
        EndProgress(Text($"已打开 {loaded.Title}。", $"Opened {loaded.Title}."));
    }

    private async Task PersistCurrentSessionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (Document.Segments.Count > 0 ||
            Document.Speakers.Count > 0 ||
            Document.AudioTracks.Count > 0)
        {
            await _store.SaveAsync(Document, cancellationToken).ConfigureAwait(true);
            _isDocumentPersisted = true;
        }
    }

    private TranscriptDocument CreateDefaultDocument()
        => new()
        {
            Title = Text(
                $"会议 {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
                $"Meeting {DateTimeOffset.Now:yyyy-MM-dd HH:mm}")
        };

    private void ReplaceDocument(
        TranscriptDocument document,
        bool hasDefaultTitle,
        bool isPersisted)
    {
        StopPlayback();
        Document = document;
        _hasDefaultDocumentTitle = hasDefaultTitle;
        _isDocumentPersisted = isPersisted;
        OnPropertyChanged(nameof(Document));
        RefreshCollections();
    }

    public void ReloadSettings()
    {
        (_options, _sherpaOptions) = LoadOptions();
        _isChinese = LocalizationManager.Normalize(_options.Ui.Language) == "zh-CN";
        _store = new SqliteTranscriptStore(_options.Storage.DatabasePath);
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(StoragePath));
        RaiseOperationCanExecuteChanged();
        UpdateModelReadinessStatus();
    }

    private Task ToggleLanguageAsync()
    {
        _isChinese = !_isChinese;
        var settings = _settingsFileService.Load();
        settings.App.Ui.Language = _isChinese ? "zh-CN" : "en-US";
        _settingsFileService.Save(settings);
        LocalizationManager.Apply(settings.App.Ui.Language);
        OnLanguageChanged();
        StatusMessage = Text("语言已切换为中文。", "Language switched to English.");
        return Task.CompletedTask;
    }

    private void HandleCommandError(Exception exception)
    {
        DiagnosticLog.Error("Async UI command failed.", exception);
        EndProgress($"{Text("操作失败", "Operation failed")}: {exception.Message}");
    }

    private void LocalizationManager_LanguageChanged(object? sender, EventArgs e)
    {
        _isChinese = LocalizationManager.CurrentLanguage == "zh-CN";
        OnLanguageChanged();
    }

    private void BeginProgress(string status, bool indeterminate, double progress)
    {
        IsBusy = true;
        IsProgressIndeterminate = indeterminate;
        OperationProgress = progress;
        StatusMessage = status;
    }

    private void SetProgress(string status, bool indeterminate, double progress)
    {
        IsBusy = true;
        IsProgressIndeterminate = indeterminate;
        OperationProgress = progress;
        StatusMessage = status;
    }

    private void EndProgress(string status, bool completed = false)
    {
        IsBusy = false;
        if (!IsRecording)
        {
            IsProgressIndeterminate = false;
            OperationProgress = completed ? 100 : 0;
        }

        StatusMessage = status;
    }

    internal static double MapImportProgress(TranscriptionProgress progress)
    {
        return progress.Stage switch
        {
            TranscriptionStage.Preparing => 45,
            TranscriptionStage.LoadingModel => 50,
            TranscriptionStage.Transcribing => 45 + progress.Percent * 0.40,
            TranscriptionStage.PostProcessing => 85 + progress.Percent * 0.05,
            TranscriptionStage.AsrComplete => 90,
            TranscriptionStage.Diarizing => 90 + progress.Percent * 0.10,
            TranscriptionStage.Finalizing => 99,
            TranscriptionStage.Complete => 100,
            _ => 0,
        };
    }

    private string FormatTranscriptionProgress(TranscriptionProgress progress)
        => progress.Stage switch
        {
            TranscriptionStage.Preparing => Text("正在准备转写...", "Preparing transcription..."),
            TranscriptionStage.LoadingModel => Text("正在加载识别模型...", "Loading speech model..."),
            TranscriptionStage.Transcribing => Text(
                $"正在转写 {progress.Percent:0}%...",
                $"Transcribing {progress.Percent:0}%..."),
            TranscriptionStage.PostProcessing => Text("正在整理转写结果...", "Post-processing transcript..."),
            TranscriptionStage.AsrComplete => Text("转写完成，正在整理...", "Transcription complete; finalizing..."),
            TranscriptionStage.Diarizing => Text(
                $"正在识别说话人 {progress.Percent:0}%...",
                $"Identifying speakers... {progress.Percent:0}%..."),
            TranscriptionStage.Finalizing or TranscriptionStage.Complete =>
                Text("正在完成导入...", "Finalizing import..."),
            _ => Text("正在处理...", "Working...")
        };

    private string Text(string zh, string en)
        => _isChinese ? zh : en;

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(EngineStatus));
        OnPropertyChanged(nameof(StoragePath));
        OnPropertyChanged(nameof(SessionSummary));
        OnPropertyChanged(nameof(ToggleLanguageLabel));
        OnPropertyChanged(nameof(WorkbenchLabel));
        OnPropertyChanged(nameof(DevicesLabel));
        OnPropertyChanged(nameof(SystemAudioLabel));
        OnPropertyChanged(nameof(CaptureSystemAudioLabel));
        OnPropertyChanged(nameof(MicrophoneLabel));
        OnPropertyChanged(nameof(CaptureMicrophoneLabel));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(CancelOperationLabel));
        OnPropertyChanged(nameof(ExpectedSpeakersLabel));
        OnPropertyChanged(nameof(ExportLabel));
        OnPropertyChanged(nameof(HistoryLabel));
        OnPropertyChanged(nameof(SettingsLabel));
        OnPropertyChanged(nameof(TranscriptLabel));
        OnPropertyChanged(nameof(SearchTranscriptLabel));
        OnPropertyChanged(nameof(SegmentEditHint));
        OnPropertyChanged(nameof(MergePreviousLabel));
        OnPropertyChanged(nameof(DeleteSegmentLabel));
        OnPropertyChanged(nameof(PlaySegmentActionLabel));
        OnPropertyChanged(nameof(PlaySegmentLabel));
        OnPropertyChanged(nameof(StopPlaybackLabel));
        OnPropertyChanged(nameof(SpeakersLabel));
        OnPropertyChanged(nameof(SpeakersHint));
        OnPropertyChanged(nameof(NoSpeakersTitle));
        OnPropertyChanged(nameof(NoSpeakersHint));
        OnPropertyChanged(nameof(MergeSpeakerLabel));
        OnPropertyChanged(nameof(SessionLabel));
        OnPropertyChanged(nameof(ProgressLabel));
        LocalizeDefaultDocumentTitle();
        RebuildSpeakerCountOptions(SelectedSpeakerCount?.Value ?? -1);
        LocalizeDefaultSpeakerNames();
    }

    private void LocalizeDefaultDocumentTitle()
    {
        if (!_hasDefaultDocumentTitle)
        {
            return;
        }

        const string chinesePrefix = "会议 ";
        const string englishPrefix = "Meeting ";
        var suffix = Document.Title.StartsWith(chinesePrefix, StringComparison.Ordinal)
            ? Document.Title[chinesePrefix.Length..]
            : Document.Title.StartsWith(englishPrefix, StringComparison.Ordinal)
                ? Document.Title[englishPrefix.Length..]
                : null;
        if (suffix is null)
        {
            _hasDefaultDocumentTitle = false;
            return;
        }

        Document.Title = Text(chinesePrefix + suffix, englishPrefix + suffix);
        OnPropertyChanged(nameof(Document));
    }

    private void LocalizeDefaultSpeakerNames()
    {
        var changed = false;
        foreach (var speaker in Document.Speakers.ToArray())
        {
            var localized = LocalizeDefaultSpeakerName(speaker.Name);
            if (string.Equals(localized, speaker.Name, StringComparison.Ordinal))
            {
                continue;
            }

            Document.RenameSpeaker(speaker.Id, localized);
            changed = true;
        }

        if (changed)
        {
            RefreshCollections();
        }
    }

    private string LocalizeDefaultSpeakerName(string name)
    {
        if (string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase) || name == "我")
        {
            return Text("我", "Me");
        }
        if (string.Equals(name, "Remote", StringComparison.OrdinalIgnoreCase) || name == "远端")
        {
            return Text("远端", "Remote");
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            name,
            @"^(?:Speaker|说话人)\s*(?<number>\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success
            ? Text($"说话人 {match.Groups["number"].Value}", $"Speaker {match.Groups["number"].Value}")
            : name;
    }

    private void RebuildSpeakerCountOptions(int selectedValue)
    {
        SpeakerCountOptions.Clear();
        SpeakerCountOptions.Add(new SpeakerCountOption(-1, Text("自动判断", "Auto detect")));
        for (var count = 2; count <= 8; count++)
        {
            SpeakerCountOptions.Add(new SpeakerCountOption(
                count,
                Text($"{count} 人", $"{count} speakers")));
        }

        SelectedSpeakerCount = SpeakerCountOptions.First(x => x.Value == selectedValue);
    }

    private (AppOptions App, SherpaOnnxOptions Sherpa) LoadOptions()
    {
        var settings = _settingsFileService.Load();
        StoragePathResolver.Resolve(settings.App.Storage);
        return (settings.App, settings.SherpaOnnx);
    }

    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        LocalizationManager.LanguageChanged -= LocalizationManager_LanguageChanged;
        _audioPlaybackService.PlaybackStopped -= AudioPlaybackService_PlaybackStopped;
        StopPlayback();
        RaiseOperationCanExecuteChanged();
        _operationCts?.Cancel();
        try
        {
            await StopInternalAsync(false).ConfigureAwait(true);
        }
        finally
        {
            _audioPlaybackService.Dispose();
        }
    }

    private void RaiseOperationCanExecuteChanged()
    {
        StartCommand?.RaiseCanExecuteChanged();
        StopCommand?.RaiseCanExecuteChanged();
        ImportCommand?.RaiseCanExecuteChanged();
        CancelOperationCommand?.RaiseCanExecuteChanged();
        ExportCommand?.RaiseCanExecuteChanged();
        HistoryCommand?.RaiseCanExecuteChanged();
        SettingsCommand?.RaiseCanExecuteChanged();
        PlaySegmentCommand?.RaiseCanExecuteChanged();
        StopPlaybackCommand?.RaiseCanExecuteChanged();
    }

    private TimeSpan GetNextTimelineOffset()
    {
        var segmentEnd = Document.Segments.Count == 0
            ? TimeSpan.Zero
            : Document.Segments.Max(segment => segment.End);
        var audioEnd = Document.AudioTracks
            .Where(track => track.Duration is not null)
            .Select(track => track.TimelineOffset + track.Duration!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        return segmentEnd > audioEnd ? segmentEnd : audioEnd;
    }

    private bool HasRealtimeModel()
    {
        var model = _modelCatalog.FindById("streaming-paraformer-bilingual");
        return model is not null && _modelCatalog.IsInstalled(model);
    }

    private bool HasDiarizationModel()
    {
        var model = _modelCatalog.FindById("speaker-diarization");
        return model is not null && _modelCatalog.IsInstalled(model);
    }

    private bool HasOfflineModel()
    {
        var model = _modelCatalog.FindById(_sherpaOptions.ActiveModelId ?? "");
        return model is not null &&
            string.Equals(model.ExecutionMode, "offline", StringComparison.OrdinalIgnoreCase) &&
            _modelCatalog.IsInstalled(model);
    }

    private void UpdateModelReadinessStatus()
    {
        var missing = new List<string>();
        if (!HasOfflineModel())
        {
            missing.Add(Text("离线转写模型", "an offline transcription model"));
        }
        if (!HasRealtimeModel())
        {
            missing.Add(Text("实时 Paraformer", "Realtime Paraformer"));
        }
        if (!HasDiarizationModel())
        {
            missing.Add(Text("说话人分离模型", "Speaker diarization"));
        }

        StatusMessage = missing.Count == 0
            ? Text("模型已就绪。", "Models are ready.")
            : Text("请先在设置中下载：", "Download in Settings first: ") + string.Join(Text("、", ", "), missing);
    }
}

public sealed record SpeakerCountOption(int Value, string DisplayName);
