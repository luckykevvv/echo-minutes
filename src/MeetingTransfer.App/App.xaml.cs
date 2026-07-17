using System.Windows;
using MeetingTransfer.App.Configuration;
using MeetingTransfer.App.Localization;

namespace MeetingTransfer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationManager.Apply(new SettingsFileService().Load().App.Ui.Language);
        base.OnStartup(e);
    }
}
