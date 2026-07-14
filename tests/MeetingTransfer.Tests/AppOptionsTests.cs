using System.Text.Json;
using MeetingTransfer.Core.Config;

namespace MeetingTransfer.Tests;

public sealed class AppOptionsTests
{
    [Fact]
    public void NewInstall_HasIncompleteOnboardingByDefault()
    {
        var options = new AppOptions();

        Assert.False(options.Ui.OnboardingCompleted);
    }

    [Fact]
    public void ExistingSettingsWithoutUiSection_GetSafeOnboardingDefaults()
    {
        var options = JsonSerializer.Deserialize<AppOptions>("{}")!;

        Assert.NotNull(options.Ui);
        Assert.False(options.Ui.OnboardingCompleted);
    }
}
