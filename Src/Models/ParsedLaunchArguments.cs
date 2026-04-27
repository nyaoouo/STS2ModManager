using System.Collections.Generic;

namespace STS2ModManager.Models;

internal sealed class ParsedLaunchArguments
{
    public bool? ForceSteam { get; set; }

    public bool AutoSlay { get; set; }

    public string? Seed { get; set; }

    public string? LogFilePath { get; set; }

    public bool Bootstrap { get; set; }

    public string? FastMpMode { get; set; }

    public string? ClientId { get; set; }

    public bool NoMods { get; set; }

    public string? ConnectLobbyId { get; set; }

    public List<string> ExtraTokens { get; } = new();

    public string ExtraArguments { get; set; } = string.Empty;
}
