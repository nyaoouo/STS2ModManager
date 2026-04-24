using System;

namespace STS2ModManager.App.Localization;

/// <summary>
/// Composes localized strings whose shape depends on runtime values
/// (verbs, optional fields, language-specific prefixes). The simple
/// "single template" cases live as plain keys in strings.json; this
/// class handles the irregular ones that need branching logic.
/// </summary>
internal static class LocalizedFormats
{
    public static string GameNotFoundMessage(LocalizationService loc, string details)
    {
        // English: bare details. Chinese: prefixed sentence + details.
        if (loc.LanguageCode == "zhs")
        {
            return loc.Get("game.game_not_found_message_prefix") + Environment.NewLine + Environment.NewLine + details;
        }
        return details;
    }

    public static string BulkMovePrompt(LocalizationService loc, string operationVerb, int count)
    {
        var key = operationVerb == "enable" ? "mods.bulk_move_prompt.enable" : "mods.bulk_move_prompt.disable";
        return loc.Get(key, count);
    }

    public static string BulkMoveCanceledStatus(LocalizationService loc, string operationVerb)
    {
        var key = operationVerb == "enable" ? "mods.bulk_move_canceled.enable" : "mods.bulk_move_canceled.disable";
        return loc.Get(key);
    }

    public static string BulkMoveCompletedStatus(
        LocalizationService loc,
        string operationVerb,
        int changedCount,
        int unchangedCount,
        int failedCount)
    {
        var key = operationVerb == "enable" ? "mods.bulk_move_completed.enable" : "mods.bulk_move_completed.disable";
        return loc.Get(key, changedCount, unchangedCount, failedCount);
    }

    public static string OperationCanceledStatus(LocalizationService loc, string operationVerb, string modId)
    {
        var key = operationVerb switch
        {
            "enable" => "mods.operation_canceled.enable",
            "disable" => "mods.operation_canceled.disable",
            _ => "mods.operation_canceled.other",
        };
        return loc.Get(key, modId, operationVerb);
    }

    public static string MoveCompletedStatus(LocalizationService loc, string operationVerb, string modId, string modName)
    {
        var key = operationVerb switch
        {
            "enable" => "mods.move_completed.enable",
            "disable" => "mods.move_completed.disable",
            _ => "mods.move_completed.other",
        };
        return loc.Get(key, modId, modName, operationVerb);
    }

    public static string ArchiveInstalled(
        LocalizationService loc,
        string archiveFileName,
        string modId,
        string disabledDirectoryName,
        string archiveFolderName,
        string installFolderName)
    {
        var renamed = !string.Equals(archiveFolderName, installFolderName, StringComparison.OrdinalIgnoreCase);
        var hint = renamed
            ? " " + loc.Get("archive.archive_installed_rename_hint", archiveFolderName, installFolderName)
            : string.Empty;
        return loc.Get("archive.archive_installed", archiveFileName, modId, disabledDirectoryName) + hint;
    }
}
