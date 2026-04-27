using System;
using System.Globalization;

namespace STS2ModManager.Services.UI;

internal static class Localization
{
    public static bool IsSupported(string? languageCode)
    {
        return !string.IsNullOrWhiteSpace(languageCode) &&
            (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(languageCode, "chs", StringComparison.OrdinalIgnoreCase));
    }

    public static AppLanguage ParseOrDefault(string? languageCode)
    {
        if (string.Equals(languageCode, "chs", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.ChineseSimplified;
        }

        if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.English;
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ChineseSimplified
            : AppLanguage.English;
    }

    public static string ToCode(AppLanguage language)
    {
        return language == AppLanguage.ChineseSimplified ? "chs" : "en";
    }
}
