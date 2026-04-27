using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace STS2ModManager.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> to disk and provides the
/// validation/normalization helpers used by both the settings UI and the
/// update checker. Stateless apart from the settings file path.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SettingsService
{
    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath
            ?? Path.Combine(AppContext.BaseDirectory, "ModManager.settings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return AppSettings.Default;
            }

            var settings = JsonSerializer.Deserialize(
                File.ReadAllText(SettingsFilePath),
                ModManagerJsonContext.Default.AppSettings);
            if (settings is null)
            {
                return AppSettings.Default;
            }

            var disabledDirectoryValue = settings.DisabledDirectoryName;
            if (string.IsNullOrWhiteSpace(disabledDirectoryValue) || !IsValidDirectoryName(disabledDirectoryValue))
            {
                disabledDirectoryValue = AppSettings.Default.DisabledDirectoryName;
            }

            var languageCode = Localization.IsSupported(settings.LanguageCode)
                ? settings.LanguageCode
                : AppSettings.Default.LanguageCode;

            var gamePath = LaunchService.TryNormalizeGameDirectoryPath(settings.GamePath, out var normalizedGameDirectory)
                ? normalizedGameDirectory
                : null;
            var savedLaunchMode = Enum.IsDefined(typeof(LaunchMode), settings.LaunchMode)
                ? settings.LaunchMode
                : AppSettings.Default.LaunchMode;
            var savedLaunchArguments = settings.LaunchArguments?.Trim() ?? string.Empty;
            var savedSkippedUpdateVersion = NormalizeStoredVersion(settings.SkippedUpdateVersion);
            var savedUpdateRemindAfterUtc = NormalizeUtc(settings.UpdateRemindAfterUtc);

            return new AppSettings(
                disabledDirectoryValue,
                languageCode,
                gamePath,
                savedLaunchMode,
                savedLaunchArguments,
                settings.SplitModList,
                settings.CheckForUpdates,
                savedSkippedUpdateVersion,
                savedUpdateRemindAfterUtc,
                Enum.IsDefined(typeof(ThemeMode), settings.ThemeMode) ? settings.ThemeMode : ThemeMode.System,
                Math.Clamp(settings.BackupRetentionCount, 0, 100));
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, ModManagerJsonContext.Default.AppSettings);
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>
    /// Validates a single-segment directory name and returns a localized error message
    /// when invalid. Used by both the settings UI and the configuration apply path.
    /// </summary>
    public static bool TryValidateDirectoryName(string value, LocalizationService loc, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = loc.Get("ui.empty_folder_name_message");
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errorMessage = loc.Get("ui.invalid_folder_characters_message");
            return false;
        }

        if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            errorMessage = loc.Get("ui.single_folder_name_message");
            return false;
        }

        if (value is "." or "..")
        {
            errorMessage = loc.Get("ui.dot_folder_name_message");
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>Pure structural check, no localized messages.</summary>
    public static bool IsValidDirectoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return value is not "." and not "..";
    }

    /// <summary>
    /// Strips a leading "v" prefix and trims whitespace. Returns null for blank input.
    /// Used for both update-check version comparison and persisted skip-version values.
    /// </summary>
    public static string? NormalizeStoredVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(1)
            : trimmed;
    }

    public static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value;
        return normalized.ToUniversalTime();
    }
}
