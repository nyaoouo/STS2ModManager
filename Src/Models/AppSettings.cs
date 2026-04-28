using System;
using System.Text.Json.Serialization;

namespace STS2ModManager.Models;

internal sealed record AppSettings(
    string DisabledDirectoryName,
    string? LanguageCode,
    string? GamePath,
    LaunchMode LaunchMode,
    string? LaunchArguments,
    bool SplitModList = true,
    bool CheckForUpdates = true,
    string? SkippedUpdateVersion = null,
    DateTime? UpdateRemindAfterUtc = null,
    ThemeMode ThemeMode = ThemeMode.System,
    int BackupRetentionCount = 5)
{
    public static AppSettings Default { get; } = new(".archive-mods", null, null, LaunchMode.Steam, null);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class ModManagerJsonContext : JsonSerializerContext
{
}
