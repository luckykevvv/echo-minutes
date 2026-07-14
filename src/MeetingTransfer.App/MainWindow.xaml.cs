using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.App.Updates;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.App;

public partial class MainWindow : Window
{
    private bool _isShuttingDown;
    private bool _shutdownComplete;
    private bool _finalCloseStarted;
    private bool _onboardingChecked;
    private readonly UpdateCoordinator _updateCoordinator = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void SpeakerNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CommitSpeakerName(sender);

    private void SpeakerNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not Speaker speaker)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitSpeakerName(textBox);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            textBox.Text = speaker.Name;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void CommitSpeakerName(object sender)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not Speaker speaker ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CommitSpeakerName(speaker.Id, textBox.Text))
        {
            textBox.Text = speaker.Name;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_onboardingChecked || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _onboardingChecked = true;
        if (viewModel.ShouldShowOnboarding)
        {
            var onboarding = new OnboardingWindow(new Configuration.SettingsFileService())
            {
                Owner = this
            };
            onboarding.ShowDialog();
            await viewModel.CompleteOnboardingAsync().ConfigureAwait(true);
        }

        await _updateCoordinator.CheckAndPromptAsync(this, showUpToDate: false).ConfigureAwait(true);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            // A user can click Close again after cleanup completes but before
            // the queued final Close runs. Remember that WPF is already
            // closing so the queued callback does not call Close a second
            // time and trigger Window.VerifyNotClosing().
            _finalCloseStarted = true;
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        IsEnabled = false;
        try
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.ShutdownAsync().ConfigureAwait(true);
            }
        }
        catch
        {
            // Closing must never escalate a cleanup/save failure into an
            // unhandled async-void exception. The process is shutting down.
        }
        finally
        {
            _shutdownComplete = true;
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_finalCloseStarted)
                {
                    Close();
                }
            }));
        }
    }
}
