using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.App.Updates;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsFileService _settingsFileService;
    private readonly RuntimeSettings _settings;
    private readonly ModelCatalog _catalog;
    private readonly ModelCardListViewModel _modelCards;
    private readonly UpdateCoordinator _updateCoordinator = new();

    public SettingsWindow(SettingsFileService settingsFileService)
    {
        InitializeComponent();
        _settingsFileService = settingsFileService;
        _settings = _settingsFileService.Load();
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

        LoadFields();
        if (_modelCards.Cards.Count > 0)
        {
            ModelsList.SelectedIndex = 0;
        }
    }

    public ModelCardListViewModel ViewModel => _modelCards;

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
            _settings.App.Speech.Engine = "SherpaOnnx";
            _settings.App.Import.FfmpegPath = EmptyToNull(FfmpegPathBox.Text);
            _settings.SherpaOnnx.OnlineRecognizerExecutable = EmptyToNull(OnlineExeBox.Text);
            _settings.SherpaOnnx.OnlineArgumentsTemplate = EmptyToNull(OnlineArgsBox.Text);
            _settings.SherpaOnnx.WhisperCppLanguage = OfflineLanguageBox.SelectedValue as string ?? "bilingual";

            _settingsFileService.Save(_settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
            Filter = "Executables and model files|*.exe;*.onnx;*.txt|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
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
                this,
                showUpToDate: true,
                message => UpdateStatusText.Text = message).ConfigureAwait(true);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

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
