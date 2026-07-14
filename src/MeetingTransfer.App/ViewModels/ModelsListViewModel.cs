using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.App.ViewModels;

/// <summary>
/// One ViewModel per model card. Tracks its install / download / active state.
/// </summary>
public sealed class ModelCardViewModel : ObservableObject
{
    private readonly ModelCardListViewModel _owner;
    private readonly ModelDescriptor _model;
    private ModelInstallState _state;
    private double _downloadProgress;
    private string? _statusMessage;
    private CancellationTokenSource? _downloadCts;

    public ModelCardViewModel(ModelCardListViewModel owner, ModelDescriptor model)
    {
        _owner = owner;
        _model = model;
    }

    public ModelDescriptor Model => _model;
    public string Id => _model.Id;
    public string Family => _model.Family;
    public string DisplayName => _model.DisplayName;
    public string SizeDisplay => FormatSize(_model.SizeBytes);
    public string LanguagesDisplay => _model.Languages.Count == 0
        ? "—"
        : string.Join(", ", _model.Languages);
    public string SpeedNote => _model.SpeedNote;
    public string AccuracyNote => _model.AccuracyNote;
    public string Description => _model.Description;
    public string ExecutionMode => _model.ExecutionMode;
    public string CategoryLabel => ExecutionMode.ToLowerInvariant() switch
    {
        "offline" => "OFFLINE TRANSCRIPTION  ·  离线转写",
        "online" => "REALTIME TRANSCRIPTION  ·  实时转写",
        _ => "FEATURE RESOURCES  ·  功能资源",
    };
    public string Engine => _model.EngineDisplay;
    public string EngineLabel => $"Engine · {Engine}";

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
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsInstalled => State == ModelInstallState.Installed;
    public bool IsDownloading => State == ModelInstallState.Downloading;
    public bool IsActive => State == ModelInstallState.Active;
    public bool CanUseAsDefault => string.Equals(ExecutionMode, "offline", StringComparison.OrdinalIgnoreCase);
    public bool IsAvailableToUse => IsInstalled && !IsActive && CanUseAsDefault;

    public string PrimaryActionLabel => State switch
    {
        ModelInstallState.NotInstalled => $"Download · {SizeDisplay}",
        ModelInstallState.Downloading => "Cancel",
        ModelInstallState.Installed when CanUseAsDefault => "Use as default",
        ModelInstallState.Installed when string.Equals(ExecutionMode, "online", StringComparison.OrdinalIgnoreCase) => "Installed for live recording",
        ModelInstallState.Installed => "Installed for speaker labels",
        ModelInstallState.Active => "✓ Active",
        ModelInstallState.Failed => "Retry",
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
        StatusMessage = "Starting download…";
        try
        {
            var downloader = new ModelDownloader();
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusMessage = $"Downloaded {(int)(p * 100)}%";
            });
            await downloader.DownloadAsync(_model, _owner.Catalog, progress, _downloadCts.Token)
                .ConfigureAwait(true);
            DownloadProgress = 1.0;
            State = ModelInstallState.Installed;
            StatusMessage = "Installed.";
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
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            State = ModelInstallState.Failed;
            StatusMessage = ex.Message;
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
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        double mb = bytes / 1_000_000.0;
        if (mb < 1000) return $"{mb:0.#} MB";
        return $"{mb / 1000:0.##} GB";
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
public sealed class ModelCardListViewModel : ObservableObject
{
    private readonly SettingsFileService _settingsFileService;
    private string? _activeModelId;
    private ModelCardViewModel? _selectedCard;

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
