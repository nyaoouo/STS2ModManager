namespace STS2ModManager.Models;

internal enum ModFilter
{
    All,
    Enabled,
    Disabled,
}

internal enum ModMoveOutcome
{
    Changed,
    Unchanged,
    Failed,
}

internal enum UpdatePromptChoice
{
    UpdateNow,
    RemindLater,
    SkipThisVersion,
    NeverCheck,
}

internal enum ConflictChoice
{
    KeepIncoming,
    KeepExisting,
    Cancel,
}

internal enum AppLanguage
{
    English,
    ChineseSimplified,
}

internal enum LaunchMode
{
    Steam,
    Direct,
}

internal enum AppPage
{
    Mods,
    Saves,
    Config,
}

internal enum ThemeMode
{
    System,
    Light,
    Dark,
}

internal enum SaveKind
{
    Vanilla,
    Modded,
}

internal enum SaveLocationKind
{
    SteamUser,
    LocalDefault,
}
