using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Localization;
using MeetingTransfer.App.Updates;
using MeetingTransfer.App.ViewModels;
using MeetingTransfer.Core.Models;
using MeetingTransfer.Core.Transcripts;
using MeetingTransfer.Core.Updates;

namespace MeetingTransfer.App.SmokeTests;

public sealed class OnboardingRenderSmokeTests
{
    [Fact]
    public void OnboardingAndSpeakerInspector_CanRenderAndUpdateWithoutBindingExceptions()
    {
        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "meeting-transfer-onboarding-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repositoryRoot = FindRepositoryRoot();
                PrepareSettingsDirectory(repositoryRoot, temporaryDirectory);

                var app = new App();
                app.InitializeComponent();
                var catalog = new ModelCatalog(temporaryDirectory);
                var settingsFileService = new SettingsFileService(temporaryDirectory);
                var window = new OnboardingWindow(settingsFileService, catalog);
                var cards = Assert.IsType<ModelCardListViewModel>(window.DataContext);
                Assert.Equal(8, cards.Cards.Count);
                Assert.Equal(
                    [
                        "离线转写",
                        "实时转写",
                        "功能资源"
                    ],
                    cards.Cards.Select(card => card.CategoryLabel).Distinct());
                var onboardingLanguageBox = Assert.IsType<ComboBox>(window.FindName("OnboardingLanguageBox"));
                Assert.Equal("简体中文", Assert.IsType<LanguageOption>(onboardingLanguageBox.SelectedItem).ToString());
                var localizedCard = cards.Cards.Single(card => card.Id == "streaming-paraformer-bilingual");
                var sileroVad = localizedCard.Model.Files.Single(file => file.Name == "silero_vad.onnx");
                Assert.Equal(
                    "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx",
                    sileroVad.Url);
                Assert.Equal(
                    "9E2449E1087496D8D4CABA907F23E0BD3F78D91FA552479BB9C23AC09CBB1FD6",
                    sileroVad.Sha256);
                Assert.Equal("实时", localizedCard.ExecutionMode);
                Assert.Contains("实时录音必需模型", localizedCard.Description, StringComparison.Ordinal);
                Assert.Contains("下载", localizedCard.PrimaryActionLabel, StringComparison.Ordinal);

                var baseCard = cards.Cards.Single(card => card.Id == "whisper-base");
                Assert.Equal("多语言", baseCard.LanguagesDisplay);
                MarkModelInstalled(catalog, baseCard);
                var onboardingConcurrentSettings = settingsFileService.Load();
                onboardingConcurrentSettings.Models.ActiveModelId = baseCard.Id;
                onboardingConcurrentSettings.SherpaOnnx.ActiveModelId = baseCard.Id;
                settingsFileService.Save(onboardingConcurrentSettings);
                onboardingLanguageBox.SelectedValue = "en-US";
                Assert.Equal(baseCard.Id, settingsFileService.Load().Models.ActiveModelId);
                Assert.Equal("English", Assert.IsType<LanguageOption>(onboardingLanguageBox.SelectedItem).ToString());
                Assert.Equal("Multilingual", baseCard.LanguagesDisplay);
                Assert.Equal("Realtime", localizedCard.ExecutionMode);
                Assert.Contains("Required for live recording", localizedCard.Description, StringComparison.Ordinal);
                Assert.Contains("Download", localizedCard.PrimaryActionLabel, StringComparison.Ordinal);
                Assert.Contains("REALTIME TRANSCRIPTION", cards.Cards.Select(card => card.CategoryLabel));
                onboardingLanguageBox.SelectedValue = "zh-CN";

                var stepOne = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("StepOne"));
                var stepTwo = Assert.IsType<Grid>(window.FindName("StepTwo"));
                var modelsList = Assert.IsType<ListBox>(window.FindName("OnboardingModelsList"));
                var nextButton = Assert.IsType<Button>(window.FindName("NextButton"));
                var setupStatus = Assert.IsType<TextBlock>(window.FindName("ModelSetupStatus"));

                Assert.Equal(Visibility.Visible, stepOne.Visibility);
                Assert.Equal(Visibility.Collapsed, stepTwo.Visibility);
                nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, stepOne.Visibility);
                Assert.Equal(Visibility.Visible, stepTwo.Visibility);

                window.Measure(new Size(920, 640));
                window.Arrange(new Rect(0, 0, 920, 640));
                window.ApplyTemplate();
                modelsList.ApplyTemplate();
                window.UpdateLayout();

                foreach (var card in cards.Cards)
                {
                    var presenter = new ContentPresenter
                    {
                        Content = card,
                        ContentTemplate = modelsList.ItemTemplate
                    };
                    stepTwo.Children.Add(presenter);
                    presenter.ApplyTemplate();
                    presenter.Measure(new Size(570, 80));
                    presenter.Arrange(new Rect(0, 0, 570, 80));
                    presenter.UpdateLayout();
                    stepTwo.Children.Remove(presenter);
                }

                nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, stepTwo.Visibility);
                Assert.Contains("至少一个离线", setupStatus.Text, StringComparison.Ordinal);
                var speakerCard = cards.Cards.Single(card => card.Id == "speaker-diarization");
                speakerCard.State = ModelInstallState.Installed;
                nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, stepTwo.Visibility);
                Assert.Contains("不能单独用于转写", setupStatus.Text, StringComparison.Ordinal);
                baseCard.State = ModelInstallState.Installed;
                nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, Assert.IsAssignableFrom<FrameworkElement>(window.FindName("StepThree")).Visibility);

                var settingsView = new SettingsView(settingsFileService);
                var applicationLanguageBox = Assert.IsType<ComboBox>(settingsView.FindName("ApplicationLanguageBox"));
                Assert.Equal("简体中文", Assert.IsType<LanguageOption>(applicationLanguageBox.SelectedItem).ToString());
                var currentVersionText = Assert.IsType<System.Windows.Documents.Run>(settingsView.FindName("CurrentVersionText"));
                var checkForUpdatesButton = Assert.IsType<Button>(settingsView.FindName("CheckForUpdatesButton"));
                var updateStatusText = Assert.IsType<TextBlock>(settingsView.FindName("UpdateStatusText"));
                Assert.StartsWith("v", currentVersionText.Text, StringComparison.Ordinal);
                Assert.Equal("检查更新", checkForUpdatesButton.Content);
                Assert.Contains("自动检查", updateStatusText.Text, StringComparison.Ordinal);
                var smallCard = settingsView.ViewModel.Cards.Single(card => card.Id == "whisper-small");
                MarkModelInstalled(catalog, smallCard);
                var settingsConcurrentState = settingsFileService.Load();
                settingsConcurrentState.Models.ActiveModelId = smallCard.Id;
                settingsConcurrentState.SherpaOnnx.ActiveModelId = smallCard.Id;
                settingsFileService.Save(settingsConcurrentState);
                applicationLanguageBox.SelectedValue = "en-US";
                LocalizationManager.Apply("en-US");
                settingsView.UpdateLayout();
                Assert.Equal("English", Assert.IsType<LanguageOption>(applicationLanguageBox.SelectedItem).ToString());
                Assert.Equal("Check for updates", LocalizationManager.Text("CheckForUpdates"));
                Assert.Contains("automatically", LocalizationManager.Text("AutomaticUpdateHint"), StringComparison.OrdinalIgnoreCase);
                var saveSettingsButton = Assert.IsType<Button>(settingsView.FindName("SaveSettingsButton"));
                saveSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(smallCard.Id, settingsFileService.Load().Models.ActiveModelId);
                settingsView.Measure(new Size(1180, 760));
                settingsView.Arrange(new Rect(0, 0, 1180, 760));
                settingsView.UpdateLayout();
                var offlineLanguageBox = Assert.IsType<ComboBox>(settingsView.FindName("OfflineLanguageBox"));
                offlineLanguageBox.ApplyTemplate();
                var dropDownToggle = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(
                    offlineLanguageBox.Template.FindName("DropDownToggle", offlineLanguageBox));
                Assert.InRange(Math.Abs(dropDownToggle.ActualWidth - offlineLanguageBox.ActualWidth), 0, 0.5);

                var release = new ReleaseInfo(
                    "v9.0.0",
                    "EchoMinutes 9.0",
                    "A concise release note.",
                    DateTimeOffset.UtcNow,
                    new Uri("https://github.com/luckykevvv/echo-minutes/releases/tag/v9.0.0"),
                    new ReleaseAsset(GitHubReleaseClient.PackageAssetName, new Uri("https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip"), 1024),
                    new ReleaseAsset(GitHubReleaseClient.ChecksumAssetName, new Uri("https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip.sha256"), 96));
                var updateWindow = new UpdateWindow(release, new GitHubReleaseClient());
                Assert.Equal("EchoMinutes 9.0", Assert.IsType<TextBlock>(updateWindow.FindName("ReleaseTitleText")).Text);
                Assert.Equal("A concise release note.", Assert.IsType<TextBlock>(updateWindow.FindName("ReleaseNotesText")).Text);
                updateWindow.Measure(new Size(620, 520));
                updateWindow.Arrange(new Rect(0, 0, 620, 520));
                updateWindow.UpdateLayout();

                var mainWindow = new MainWindow();
                var mainViewModel = Assert.IsType<MainWindowViewModel>(mainWindow.DataContext);
                var buildVersionText = Assert.IsType<TextBlock>(mainWindow.FindName("BuildVersionText"));
                Assert.Equal(UpdateCoordinator.CurrentVersionText, buildVersionText.Text);
                var minimizeWindowButton = Assert.IsType<Button>(mainWindow.FindName("MinimizeWindowButton"));
                var maximizeWindowButton = Assert.IsType<Button>(mainWindow.FindName("MaximizeWindowButton"));
                var closeWindowButton = Assert.IsType<Button>(mainWindow.FindName("CloseWindowButton"));
                Assert.Equal("Minimize", minimizeWindowButton.ToolTip);
                Assert.Equal("Maximize", maximizeWindowButton.ToolTip);
                Assert.Equal("Close", closeWindowButton.ToolTip);
                var emptyPanel = Assert.IsType<Border>(mainWindow.FindName("NoSpeakersPanel"));
                var speakersListPanel = Assert.IsType<ScrollViewer>(mainWindow.FindName("SpeakersListPanel"));
                var speakerItems = Assert.IsType<ItemsControl>(mainWindow.FindName("SpeakerItemsControl"));
                emptyPanel.DataContext = mainViewModel;
                speakersListPanel.DataContext = mainViewModel;
                speakerItems.DataContext = mainViewModel;
                var emptyBinding = BindingOperations.GetBinding(emptyPanel, UIElement.VisibilityProperty);
                var listBinding = BindingOperations.GetBinding(speakersListPanel, UIElement.VisibilityProperty);
                var itemsBinding = BindingOperations.GetBinding(speakerItems, ItemsControl.ItemsSourceProperty);
                Assert.NotNull(emptyBinding);
                Assert.NotNull(listBinding);
                Assert.NotNull(itemsBinding);
                Assert.Equal(nameof(MainWindowViewModel.HasNoSpeakers), emptyBinding.Path.Path);
                Assert.Equal(nameof(MainWindowViewModel.HasSpeakers), listBinding.Path.Path);
                Assert.Equal(nameof(MainWindowViewModel.Speakers), itemsBinding.Path.Path);
                mainWindow.Measure(new Size(1360, 860));
                mainWindow.Arrange(new Rect(0, 0, 1360, 860));
                mainWindow.UpdateLayout();
                Assert.False(mainViewModel.HasSpeakers);
                Assert.True(mainViewModel.HasNoSpeakers);

                var workspaceView = Assert.IsType<Grid>(mainWindow.FindName("WorkspaceView"));
                var settingsHost = Assert.IsType<Grid>(mainWindow.FindName("SettingsHost"));
                mainViewModel.SettingsCommand.Execute(null);
                mainWindow.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, workspaceView.Visibility);
                Assert.Equal(Visibility.Visible, settingsHost.Visibility);
                var embeddedSettings = Assert.IsType<SettingsView>(Assert.Single(settingsHost.Children));
                var disposedCard = embeddedSettings.ViewModel.Cards[0];
                var disposedCardNotifications = 0;
                disposedCard.PropertyChanged += (_, _) => disposedCardNotifications++;
                var backButton = Assert.IsType<Button>(embeddedSettings.FindName("BackToWorkspaceButton"));
                backButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                mainWindow.UpdateLayout();
                Assert.Equal(Visibility.Visible, workspaceView.Visibility);
                Assert.Equal(Visibility.Collapsed, settingsHost.Visibility);
                disposedCardNotifications = 0;
                LocalizationManager.Apply("zh-CN");
                Assert.Equal(0, disposedCardNotifications);

                var historyHost = Assert.IsType<Grid>(mainWindow.FindName("HistoryHost"));
                mainViewModel.HistoryCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                mainWindow.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, workspaceView.Visibility);
                Assert.Equal(Visibility.Visible, historyHost.Visibility);
                var embeddedHistory = Assert.IsType<SessionHistoryView>(Assert.Single(historyHost.Children));
                embeddedHistory.Measure(new Size(1100, 720));
                embeddedHistory.Arrange(new Rect(0, 0, 1100, 720));
                embeddedHistory.UpdateLayout();
                var historyBackButton = FindButtonByContent(
                    embeddedHistory,
                    LocalizationManager.Text("BackWorkspace"));
                historyBackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                mainWindow.UpdateLayout();
                Assert.Equal(Visibility.Visible, workspaceView.Visibility);
                Assert.Equal(Visibility.Collapsed, historyHost.Visibility);

                var firstSpeaker = mainViewModel.Document.EnsureSpeaker("speaker-1", "Speaker 1");
                var firstSegment = new TranscriptSegment
                {
                    SpeakerId = firstSpeaker.Id,
                    SpeakerName = firstSpeaker.Name,
                    Text = "First segment"
                };
                mainViewModel.Document.Segments.Add(firstSegment);
                RefreshMainCollections(mainViewModel);
                mainWindow.Measure(new Size(1360, 860));
                mainWindow.Arrange(new Rect(0, 0, 1360, 860));
                mainWindow.UpdateLayout();
                Assert.True(mainViewModel.HasSpeakers);
                Assert.False(mainViewModel.HasNoSpeakers);
                Assert.Single(mainViewModel.Speakers);

                LocalizationManager.Apply("zh-CN");
                Assert.Equal("最小化", minimizeWindowButton.ToolTip);
                Assert.Equal("最大化", maximizeWindowButton.ToolTip);
                Assert.Equal("关闭", closeWindowButton.ToolTip);
                Assert.StartsWith("会议 ", mainViewModel.Document.Title, StringComparison.Ordinal);
                Assert.Equal("说话人 1", firstSpeaker.Name);
                LocalizationManager.Apply("en-US");
                Assert.StartsWith("Meeting ", mainViewModel.Document.Title, StringComparison.Ordinal);
                Assert.Equal("Speaker 1", firstSpeaker.Name);

                Assert.True(mainViewModel.CommitSpeakerName(firstSpeaker.Id, "Alice"));
                Assert.Equal("Alice", firstSpeaker.Name);
                Assert.Equal("Alice", firstSegment.SpeakerName);

                var secondSpeaker = mainViewModel.Document.EnsureSpeaker("speaker-2", "Speaker 2");
                mainViewModel.Document.Segments.Add(new TranscriptSegment
                {
                    SpeakerId = secondSpeaker.Id,
                    SpeakerName = secondSpeaker.Name,
                    Text = "Second segment"
                });
                RefreshMainCollections(mainViewModel);
                Assert.False(mainViewModel.MergeSpeakerCommand.CanExecute(firstSpeaker));
                Assert.True(mainViewModel.MergeSpeakerCommand.CanExecute(secondSpeaker));
                mainViewModel.IsBusy = true;
                Assert.False(mainViewModel.MergeSpeakerCommand.CanExecute(secondSpeaker));
                mainViewModel.IsBusy = false;
                mainViewModel.IsRecording = true;
                Assert.False(mainViewModel.MergeSpeakerCommand.CanExecute(secondSpeaker));
                mainViewModel.IsRecording = false;
                mainViewModel.MergeSpeakerCommand.ExecuteAsync(secondSpeaker).GetAwaiter().GetResult();
                Assert.Single(mainViewModel.Document.Speakers);
                Assert.All(mainViewModel.Document.Segments, segment => Assert.Equal(firstSpeaker.Id, segment.SpeakerId));

                var segmentToMerge = mainViewModel.Document.Segments.Last();
                mainViewModel.MergePreviousSegmentCommand.ExecuteAsync(segmentToMerge).GetAwaiter().GetResult();
                var mergedSegment = Assert.Single(mainViewModel.Document.Segments);
                Assert.True(mainViewModel.CommitSegmentText(mergedSegment.Id, "Hello world again"));
                Assert.True(mainViewModel.SplitSegment(mergedSegment.Id, 5));
                Assert.Equal(2, mainViewModel.Document.Segments.Count);
                mainViewModel.SegmentSearchText = "again";
                Assert.Single(mainViewModel.SegmentsView.Cast<TranscriptSegment>());
                mainViewModel.SegmentSearchText = string.Empty;
                var secondHalf = mainViewModel.Document.Segments.OrderBy(segment => segment.Start).Last();
                mainViewModel.MergePreviousSegmentCommand.ExecuteAsync(secondHalf).GetAwaiter().GetResult();
                Assert.Equal("Hello world again", Assert.Single(mainViewModel.Document.Segments).Text);
                mainViewModel.DeleteSegmentCommand.ExecuteAsync(Assert.Single(mainViewModel.Document.Segments)).GetAwaiter().GetResult();
                Assert.Empty(mainViewModel.Document.Segments);
                Assert.Empty(mainViewModel.Document.Speakers);

                var previousSessionId = mainViewModel.Document.SessionId;
                mainViewModel.NewSessionAsync().GetAwaiter().GetResult();
                Assert.NotEqual(previousSessionId, mainViewModel.Document.SessionId);
                Assert.DoesNotContain(
                    mainViewModel.SessionStore.ListSessionsAsync().GetAwaiter().GetResult(),
                    session => session.SessionId == previousSessionId);

                completed = true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, recursive: true);
                    }
                }
                catch
                {
                    // A failed temp cleanup must not hide the render result.
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The background WPF render smoke test timed out.");
        Assert.Null(failure);
        Assert.True(completed);
    }

    private static void PrepareSettingsDirectory(string repositoryRoot, string destination)
    {
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Path.Combine(destination, "Models"));
        File.Copy(Path.Combine(repositoryRoot, "appsettings.example.json"), Path.Combine(destination, "appsettings.example.json"));
        File.Copy(Path.Combine(repositoryRoot, "models.example.json"), Path.Combine(destination, "models.example.json"));
        File.Copy(
            Path.Combine(repositoryRoot, "src", "MeetingTransfer.Core", "Models", "catalog.json"),
            Path.Combine(destination, "Models", "catalog.json"));
    }

    private static void RefreshMainCollections(MainWindowViewModel viewModel)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "RefreshCollections",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, null);
    }

    private static void MarkModelInstalled(ModelCatalog catalog, ModelCardViewModel card)
    {
        foreach (var file in card.Model.Files)
        {
            var path = catalog.GetInstalledFilePath(card.Model, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
    }

    private static Button FindButtonByContent(DependencyObject root, object expectedContent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button && Equals(button.Content, expectedContent))
            {
                return button;
            }

            try
            {
                return FindButtonByContent(child, expectedContent);
            }
            catch (InvalidOperationException)
            {
                // Continue searching sibling branches.
            }
        }

        throw new InvalidOperationException($"Button '{expectedContent}' was not found.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MeetingTransfer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MeetingTransfer.sln from the smoke-test output directory.");
    }
}
