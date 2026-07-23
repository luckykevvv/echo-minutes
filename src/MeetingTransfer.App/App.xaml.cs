using System.IO;
using System.Windows;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Diagnostics;
using MeetingTransfer.App.Localization;
using MeetingTransfer.Core.Config;

namespace MeetingTransfer.App;

public partial class App : Application
{
    private static IReadOnlyList<string> _recoveredConfigurationFiles = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        var settingsFileService = new SettingsFileService();
        var settings = settingsFileService.Load();
        StoragePathResolver.Resolve(settings.App.Storage);
        DiagnosticLog.Initialize(settings.App.Storage.LogDirectory);
        LocalizationManager.Apply(settings.App.Ui.Language);
        DispatcherUnhandledException += (_, args) =>
            DiagnosticLog.Error("Unhandled WPF dispatcher exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagnosticLog.Error("Unhandled application-domain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            DiagnosticLog.Error("Unobserved task exception.", args.Exception);
        base.OnStartup(e);

        if (settingsFileService.RecoveredFiles.Count > 0)
        {
            DiagnosticLog.Info($"Recovered configuration files: {string.Join(", ", settingsFileService.RecoveredFiles.Select(Path.GetFileName))}");
            _recoveredConfigurationFiles = settingsFileService.RecoveredFiles.ToArray();
        }
    }

    internal static void ShowPendingConfigurationRecovery(Window owner)
    {
        if (_recoveredConfigurationFiles.Count == 0)
        {
            return;
        }

        var files = _recoveredConfigurationFiles;
        _recoveredConfigurationFiles = [];
        MessageBox.Show(
            owner,
            string.Format(
                LocalizationManager.Text("ConfigRecoveredMessage"),
                string.Join(Environment.NewLine, files)),
            LocalizationManager.Text("ConfigRecoveredTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Info($"EchoMinutes exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}
