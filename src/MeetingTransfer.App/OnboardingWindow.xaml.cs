using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Core.Models;

namespace MeetingTransfer.App;

public partial class OnboardingWindow : Window
{
    private readonly FrameworkElement[] _steps;
    private readonly ModelCardListViewModel _modelCards;
    private int _stepIndex;

    public OnboardingWindow(SettingsFileService settingsFileService, ModelCatalog? modelCatalog = null)
    {
        InitializeComponent();
        var settings = settingsFileService.Load();
        var catalog = modelCatalog ?? new ModelCatalog();
        _modelCards = new ModelCardListViewModel(catalog, settingsFileService, settings.Models.ActiveModelId);
        _modelCards.PrimaryActionCommand = new RelayCommand(async card =>
        {
            if (card is not ModelCardViewModel modelCard)
            {
                return;
            }

            if (modelCard.IsDownloading)
            {
                modelCard.CancelDownload();
            }
            else if (modelCard.IsAvailableToUse)
            {
                _modelCards.SetActiveModel(modelCard.Id);
            }
            else if (!modelCard.IsInstalled || modelCard.State == ModelInstallState.Failed)
            {
                await modelCard.StartDownloadAsync().ConfigureAwait(true);
            }

            UpdateModelSetupStatus();
        });
        _modelCards.DeleteCommand = new RelayCommand(_ => Task.CompletedTask);
        DataContext = _modelCards;
        _steps = [StepOne, StepTwo, StepThree];
        ShowStep(0, animate: false);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex == 1)
        {
            if (_modelCards.Cards.Any(card => card.IsDownloading))
            {
                ModelSetupStatus.Text = "请等待当前下载完成，或在模型按钮上取消下载。";
                ModelSetupStatus.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                return;
            }

            if (!_modelCards.Cards.Any(card => card.IsInstalled || card.IsActive))
            {
                ModelSetupStatus.Text = "请先选择并安装至少一个模型；如果暂时不下载，可以跳过引导。";
                ModelSetupStatus.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                return;
            }
        }

        if (_stepIndex < _steps.Length - 1)
        {
            ShowStep(_stepIndex + 1, animate: true);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex > 0)
        {
            ShowStep(_stepIndex - 1, animate: true);
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        CancelDownloads();
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CancelDownloads();
        base.OnClosing(e);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Skip_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ShowStep(int index, bool animate)
    {
        _stepIndex = Math.Clamp(index, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _stepIndex ? Visibility.Visible : Visibility.Collapsed;
        }

        if (animate)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _steps[_stepIndex].BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            _steps[_stepIndex].Opacity = 1;
        }

        StepCounter.Text = $"{_stepIndex + 1:00} / {_steps.Length:00}";
        BackButton.Visibility = _stepIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _stepIndex == _steps.Length - 1 ? "开始使用" : "下一步";
        NextButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            _stepIndex == _steps.Length - 1 ? "完成引导并开始使用" : "进入下一步");

        var badges = new[] { StepBadgeOne, StepBadgeTwo, StepBadgeThree };
        var labels = new[] { StepLabelOne, StepLabelTwo, StepLabelThree };
        for (var i = 0; i < badges.Length; i++)
        {
            var active = i == _stepIndex;
            badges[i].Background = active
                ? (System.Windows.Media.Brush)FindResource("AccentSurfaceBrush")
                : (System.Windows.Media.Brush)FindResource("SurfaceRaisedBrush");
            badges[i].BorderBrush = active
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("LineStrongBrush");
            labels[i].Foreground = active
                ? (System.Windows.Media.Brush)FindResource("InkBrush")
                : (System.Windows.Media.Brush)FindResource("MutedBrush");
        }

        NextButton.Focus();
    }

    private void UpdateModelSetupStatus()
    {
        var installed = _modelCards.Cards.Count(card => card.IsInstalled || card.IsActive);
        ModelSetupStatus.Text = installed == 0
            ? "至少安装一个模型才能继续；也可以跳过引导，稍后再设置。"
            : $"已准备 {installed} 个模型。离线模型标记为 Active 时即可用于导入。";
        ModelSetupStatus.Foreground = installed == 0
            ? (System.Windows.Media.Brush)FindResource("SubtleBrush")
            : (System.Windows.Media.Brush)FindResource("SuccessBrush");
    }

    private void CancelDownloads()
    {
        foreach (var card in _modelCards.Cards.Where(card => card.IsDownloading))
        {
            card.CancelDownload();
        }
    }
}
