using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Localization;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.App.Updates;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.App;

public partial class SettingsView : UserControl, IDisposable
{
    private readonly SettingsFileService _settingsFileService;
    private readonly RuntimeSettings _settings;
    private readonly ModelCatalog _catalog;
    private readonly ModelCardListViewModel _modelCards;
    private readonly UpdateCoordinator _updateCoordinator = new();
    private readonly string _originalLanguage;
    private bool _languageSaved;
    private bool _disposed;

    public SettingsView(SettingsFileService settingsFileService)
    {
        InitializeComponent();
        _settingsFileService = settingsFileService;
        _settings = _settingsFileService.Load();
        _originalLanguage = LocalizationManager.Normalize(_settings.App.Ui.Language);
        _catalog = new ModelCatalog();
        _modelCards = new ModelCardListViewModel(_catalog, _settingsFileService, _settings.Models.ActiveModelId);

        // Wire up the card list commands
        _modelCards.PrimaryActionCommand = new RelayCommand(async card =>
        {
            if (card is ModelCardViewModel c)
            {
                if (c.IsDownloading)
                {
                    c.CancelDownload();
                }
                else if (c.IsAvailableToUse)
                {
                    _modelCards.SetActiveModel(c.Id);
                }
                else if (!c.IsInstalled || c.State == ModelInstallState.Failed)
                {
                    await c.StartDownloadAsync();
                }
            }
            await Task.CompletedTask;
        });
        _modelCards.DeleteCommand = new RelayCommand(card =>
        {
            if (card is ModelCardViewModel c) c.Delete();
            return Task.CompletedTask;
        });

        DataContext = _modelCards;
        SettingsPathText.Text = $"{_settingsFileService.AppSettingsPath} | {_settingsFileService.ModelsPath}";
        CurrentVersionText.Text = UpdateCoordinator.CurrentVersionText;
        ApplicationLanguageBox.ItemsSource = LocalizationManager.SupportedLanguages;
        ApplicationLanguageBox.SelectedValue = _originalLanguage;

        LoadFields();
        if (_modelCards.Cards.Count > 0)
        {
            ModelsList.SelectedIndex = 0;
        }
    }

    public ModelCardListViewModel ViewModel => _modelCards;
    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsSaved;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _modelCards.Dispose();
    }

    private void LoadFields()
    {
        FfmpegPathBox.Text = _settings.App.Import.FfmpegPath ?? "";
        OnlineExeBox.Text = _settings.SherpaOnnx.OnlineRecognizerExecutable ?? "";
        OnlineArgsBox.Text = _settings.SherpaOnnx.OnlineArgumentsTemplate ?? "";
        OfflineLanguageBox.SelectedValue = NormalizeOfflineLanguage(_settings.SherpaOnnx.WhisperCppLanguage);
    }

    private void ModelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelsList.SelectedItem is ModelCardViewModel card)
        {
            _modelCards.SelectedCard = card;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Downloads and active-model changes persist independently while
            // this view is open. Merge edits into the latest file state so Save
            // cannot restore the model snapshot loaded by the constructor.
            var latest = _settingsFileService.Load();
            latest.App.Speech.Engine = "SherpaOnnx";
            latest.App.Import.FfmpegPath = EmptyToNull(FfmpegPathBox.Text);
            latest.SherpaOnnx.OnlineRecognizerExecutable = EmptyToNull(OnlineExeBox.Text);
            latest.SherpaOnnx.OnlineArgumentsTemplate = EmptyToNull(OnlineArgsBox.Text);
            latest.SherpaOnnx.WhisperCppLanguage = OfflineLanguageBox.SelectedValue as string ?? "bilingual";
            latest.App.Ui.Language = LocalizationManager.Normalize(ApplicationLanguageBox.SelectedValue as string);

            _settingsFileService.Save(latest);
            _languageSaved = true;
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, ex.Message, LocalizationManager.Text("SaveSettingsFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        RestoreLanguageIfNeeded();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        RestoreLanguageIfNeeded();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplicationLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplicationLanguageBox.SelectedValue is string language)
        {
            LocalizationManager.Apply(language);
        }
    }

    private void RestoreLanguageIfNeeded()
    {
        if (!_languageSaved)
        {
            LocalizationManager.Apply(_originalLanguage);
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string textBoxName ||
            FindName(textBoxName) is not TextBox textBox)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            Filter = $"{LocalizationManager.Text("ExecutableModelFiles")}|*.exe;*.onnx;*.txt|" +
                     $"{LocalizationManager.Text("AllFiles")}|*.*"
        };

        if (dialog.ShowDialog(OwnerWindow) == true)
        {
            textBox.Text = dialog.FileName;
        }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        try
        {
            await _updateCoordinator.CheckAndPromptAsync(
                OwnerWindow,
                showUpToDate: true,
                message => UpdateStatusText.Text = message).ConfigureAwait(true);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private Window OwnerWindow
        => Window.GetWindow(this)
           ?? Application.Current.MainWindow
           ?? throw new InvalidOperationException("Settings must be hosted inside the main window.");

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeOfflineLanguage(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "zh" or "chinese" => "zh",
            "en" or "english" => "en",
            _ => "bilingual",
        };
    }
}
