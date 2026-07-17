using System.Globalization;
using System.Windows;

namespace MeetingTransfer.App.Localization;

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class LocalizationManager
{
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    ];

    public static string CurrentLanguage { get; private set; } = "zh-CN";
    public static event EventHandler? LanguageChanged;

    public static string Normalize(string? language)
        => language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en-US" : "zh-CN";

    public static void Apply(string? language)
    {
        var normalized = Normalize(language);
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"/MeetingTransfer.App;component/Localization/Strings.{normalized}.xaml", UriKind.Relative)
        };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string key)
        => Application.Current.TryFindResource(key)?.ToString() ?? key;

    public static string Text(string key, string fallback)
    {
        var value = Application.Current.TryFindResource(key)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Text(key), args);
}
