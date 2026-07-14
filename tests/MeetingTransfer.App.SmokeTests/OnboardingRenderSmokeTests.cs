using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MeetingTransfer.App.Configuration;
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
                var window = new OnboardingWindow(new SettingsFileService(temporaryDirectory), catalog);
                var cards = Assert.IsType<ModelCardListViewModel>(window.DataContext);
                Assert.Equal(8, cards.Cards.Count);
                Assert.Equal(
                    [
                        "OFFLINE TRANSCRIPTION  ·  离线转写",
                        "REALTIME TRANSCRIPTION  ·  实时转写",
                        "FEATURE RESOURCES  ·  功能资源"
                    ],
                    cards.Cards.Select(card => card.CategoryLabel).Distinct());

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
                Assert.Contains("至少一个模型", setupStatus.Text, StringComparison.Ordinal);

                var settingsWindow = new SettingsWindow(new SettingsFileService(temporaryDirectory));
                var currentVersionText = Assert.IsType<System.Windows.Documents.Run>(settingsWindow.FindName("CurrentVersionText"));
                var checkForUpdatesButton = Assert.IsType<Button>(settingsWindow.FindName("CheckForUpdatesButton"));
                var updateStatusText = Assert.IsType<TextBlock>(settingsWindow.FindName("UpdateStatusText"));
                Assert.StartsWith("v", currentVersionText.Text, StringComparison.Ordinal);
                Assert.Equal("Check for updates", checkForUpdatesButton.Content);
                Assert.Contains("automatically", updateStatusText.Text, StringComparison.OrdinalIgnoreCase);
                settingsWindow.Measure(new Size(1180, 760));
                settingsWindow.Arrange(new Rect(0, 0, 1180, 760));
                settingsWindow.UpdateLayout();

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
                mainViewModel.MergeSpeakerCommand.Execute(secondSpeaker);
                Assert.Single(mainViewModel.Document.Speakers);
                Assert.All(mainViewModel.Document.Segments, segment => Assert.Equal(firstSpeaker.Id, segment.SpeakerId));

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
