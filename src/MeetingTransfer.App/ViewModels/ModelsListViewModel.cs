using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Localization;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.App.ViewModels;

/// <summary>
/// One ViewModel per model card. Tracks its install / download / active state.
/// </summary>
public sealed class ModelCardViewModel : ObservableObject, IDisposable
{
    private readonly ModelCardListViewModel _owner;
    private readonly ModelDescriptor _model;
    private ModelInstallState _state;
    private double _downloadProgress;
    private string? _statusMessage;
    private string? _statusResourceKey;
    private object[]? _statusResourceArguments;
    private CancellationTokenSource? _downloadCts;
    private bool _disposed;

    public ModelCardViewModel(ModelCardListViewModel owner, ModelDescriptor model)
    {
        _owner = owner;
        _model = model;
        LocalizationManager.LanguageChanged += LocalizationManager_LanguageChanged;
    }

    public ModelDescriptor Model => _model;
    public string Id => _model.Id;
    public string Family => _model.Family;
    public string DisplayName => Localized("Name", _model.DisplayName);
    public string SizeDisplay => FormatSize(_model.SizeBytes);
    public string LanguagesDisplay => _model.Languages.Count == 0
        ? "—"
        : string.Join(", ", _model.Languages.Select(LanguageName));
    public string SpeedNote => Localized("Speed", _model.SpeedNote);
    public string AccuracyNote => Localized("Accuracy", _model.AccuracyNote);
    public string Description => Localized("Description", _model.Description);
    public string ExecutionMode => LocalizationManager.Text($"Mode.{_model.ExecutionMode}", _model.ExecutionMode);
    public string CategoryLabel => _model.ExecutionMode.ToLowerInvariant() switch
    {
        "offline" => LocalizationManager.Text("Category.Offline"),
        "online" => LocalizationManager.Text("Category.Realtime"),
        _ => LocalizationManager.Text("Category.Features"),
    };
    public string Engine => _model.EngineDisplay;
    public string EngineLabel => $"{LocalizationManager.Text("EngineLabel")} · {Engine}";

    public string Backend => string.IsNullOrWhiteSpace(_model.Backend) ? "CPU" : _model.Backend;
    public bool IsGpuBackend => string.Equals(Backend, "GPU", StringComparison.OrdinalIgnoreCase);
    public bool IsCpuBackend => !IsGpuBackend;

    public ModelInstallState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsAvailableToUse));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(PrimaryActionLabel));
                OnPropertyChanged(nameof(CanPrimary));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    public string? StatusMessage
    {
        get => _statusResourceKey is null
            ? _statusMessage
            : LocalizationManager.Format(_statusResourceKey, _statusResourceArguments ?? []);
        set
        {
            _statusResourceKey = null;
            _statusResourceArguments = null;
            SetProperty(ref _statusMessage, value);
        }
    }

    public bool IsInstalled => State == ModelInstallState.Installed;
    public bool IsDownloading => State == ModelInstallState.Downloading;
    public bool IsActive => State == ModelInstallState.Active;
    public bool CanUseAsDefault => string.Equals(_model.ExecutionMode, "offline", StringComparison.OrdinalIgnoreCase);
    public bool IsAvailableToUse => IsInstalled && !IsActive && CanUseAsDefault;

    public string PrimaryActionLabel => State switch
    {
        ModelInstallState.NotInstalled => $"{LocalizationManager.Text("ModelAction.Download")} · {SizeDisplay}",
        ModelInstallState.Downloading => LocalizationManager.Text("ModelAction.Cancel"),
        ModelInstallState.Installed when CanUseAsDefault => LocalizationManager.Text("ModelAction.UseDefault"),
        ModelInstallState.Installed when string.Equals(_model.ExecutionMode, "online", StringComparison.OrdinalIgnoreCase) => LocalizationManager.Text("ModelAction.InstalledRealtime"),
        ModelInstallState.Installed => LocalizationManager.Text("ModelAction.InstalledFeature"),
        ModelInstallState.Active => LocalizationManager.Text("ModelAction.Active"),
        ModelInstallState.Failed => LocalizationManager.Text("ModelAction.Retry"),
        _ => "—",
    };

    public bool CanPrimary => State is ModelInstallState.NotInstalled
        or ModelInstallState.Downloading
        or ModelInstallState.Failed ||
        State == ModelInstallState.Installed && CanUseAsDefault;

    public bool CanDelete => _owner.Catalog.CanDeleteInstalled(_model) && !IsDownloading;

    public void CancelDownload()
    {
        try { _downloadCts?.Cancel(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalizationManager.LanguageChanged -= LocalizationManager_LanguageChanged;
        CancelDownload();
    }

    public async Task StartDownloadAsync()
    {
        if (IsDownloading)
        {
            CancelDownload();
            return;
        }
        _downloadCts = new CancellationTokenSource();
        State = ModelInstallState.Downloading;
        DownloadProgress = 0;
        SetLocalizedStatus("ModelStatus.Starting");
        try
        {
            var downloader = new ModelDownloader();
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                SetLocalizedStatus("ModelStatus.Progress", (int)(p * 100));
            });
            await downloader.DownloadAsync(_model, _owner.Catalog, progress, _downloadCts.Token)
                .ConfigureAwait(true);
            DownloadProgress = 1.0;
            State = ModelInstallState.Installed;
            SetLocalizedStatus("ModelStatus.Installed");
            // If this is the first install, auto-activate it.
            if (_owner.ActiveModelId is null && CanUseAsDefault)
            {
                _owner.SetActiveModel(Id);
            }
        }
        catch (OperationCanceledException)
        {
            State = ModelInstallState.NotInstalled;
            DownloadProgress = 0;
            SetLocalizedStatus("ModelStatus.Cancelled");
        }
        catch (Exception ex)
        {
            State = ModelInstallState.Failed;
            SetLocalizedStatus("ModelStatus.DownloadFailed", ex.Message);
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    public void Delete()
    {
        try
        {
            var wasActive = IsActive;
            _owner.Catalog.DeleteInstalled(_model);
            if (wasActive)
            {
                _owner.ClearActiveModel();
            }
            else
            {
                State = ModelInstallState.NotInstalled;
            }
            DownloadProgress = 0;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            SetLocalizedStatus("ModelStatus.DeleteFailed", ex.Message);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        double mb = bytes / 1_000_000.0;
        if (mb < 1000) return $"{mb:0.#} MB";
        return $"{mb / 1000:0.##} GB";
    }

    private string Localized(string field, string fallback)
        => LocalizationManager.Text($"Model.{_model.Id}.{field}", fallback);

    private static string LanguageName(string code)
        => LocalizationManager.Text($"Language.{code}", code);

    private void SetLocalizedStatus(string key, params object[] arguments)
    {
        _statusMessage = null;
        _statusResourceKey = key;
        _statusResourceArguments = arguments;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void LocalizationManager_LanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(LanguagesDisplay));
        OnPropertyChanged(nameof(SpeedNote));
        OnPropertyChanged(nameof(AccuracyNote));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ExecutionMode));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(EngineLabel));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(StatusMessage));
        _owner.RefreshGrouping();
    }
}

public enum ModelInstallState
{
    NotInstalled,
    Downloading,
    Installed,
    Active,
    Failed,
}

/// <summary>
/// Hosts the model card collection + the active-model selection.
/// </summary>
public sealed class ModelCardListViewModel : ObservableObject, IDisposable
{
    private readonly SettingsFileService _settingsFileService;
    private string? _activeModelId;
    private ModelCardViewModel? _selectedCard;
    private bool _disposed;

    public ModelCardListViewModel(ModelCatalog catalog, SettingsFileService settingsFileService, string? activeModelId)
    {
        Catalog = catalog;
        _settingsFileService = settingsFileService;
        _activeModelId = activeModelId;
        Cards = new ObservableCollection<ModelCardViewModel>(
            catalog.All.Select(m => new ModelCardViewModel(this, m)));
        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModelCardViewModel.CategoryLabel)));
        RefreshAllStates();
    }

    public ModelCatalog Catalog { get; }
    public ObservableCollection<ModelCardViewModel> Cards { get; }
    public ICollectionView CardsView { get; }

    public RelayCommand PrimaryActionCommand { get; set; } = null!;
    public RelayCommand DeleteCommand { get; set; } = null!;

    public ModelCardViewModel? SelectedCard
    {
        get => _selectedCard;
        set => SetProperty(ref _selectedCard, value);
    }

    public string? ActiveModelId
    {
        get => _activeModelId;
        private set
        {
            if (SetProperty(ref _activeModelId, value))
            {
                foreach (var card in Cards)
                {
                    OnCardStateChanged(card);
                }
            }
        }
    }

    public void RefreshAllStates()
    {
        foreach (var card in Cards)
        {
            OnCardStateChanged(card);
        }
    }

    public void RefreshGrouping()
    {
        CardsView.Refresh();
        OnPropertyChanged(nameof(SelectedCard));
    }

    public void SetActiveModel(string id)
    {
        var model = Catalog.FindById(id);
        if (model is null ||
            !string.Equals(model.ExecutionMode, "offline", StringComparison.OrdinalIgnoreCase) ||
            !Catalog.IsInstalled(model))
        {
            return;
        }
        ActiveModelId = id;
        // Persist
        var settings = _settingsFileService.Load();
        settings.Models.ActiveModelId = id;
        _settingsFileService.Save(settings);
    }

    public void ClearActiveModel()
    {
        ActiveModelId = null;
        var settings = _settingsFileService.Load();
        settings.Models.ActiveModelId = null;
        _settingsFileService.Save(settings);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var card in Cards)
        {
            card.Dispose();
        }
    }

    private void OnCardStateChanged(ModelCardViewModel card)
    {
        if (card.Id == _activeModelId)
        {
            card.State = ModelInstallState.Active;
            return;
        }
        if (Catalog.IsInstalled(card.Model))
        {
            card.State = ModelInstallState.Installed;
            return;
        }
        // Preserve Downloading / Failed transient states if currently in flight.
        if (card.State is ModelInstallState.Downloading or ModelInstallState.Failed)
        {
            return;
        }
        card.State = ModelInstallState.NotInstalled;
    }
}
