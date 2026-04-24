using STS2ModManager.App;

internal sealed record ModManagerConfig(
    string? GamePath,
    string DisabledDirectoryName,
    AppLanguage Language,
    LaunchMode LaunchMode,
    string LaunchArguments,
    bool SplitModList = true,
    ThemeMode ThemeMode = ThemeMode.System,
    int BackupRetentionCount = 5);

internal sealed record LanguageOption(AppLanguage Language, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record LaunchModeOption(LaunchMode LaunchMode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record ThemeModeOption(ThemeMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}
