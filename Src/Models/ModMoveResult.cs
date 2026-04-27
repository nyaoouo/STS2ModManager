namespace STS2ModManager.Models;

internal sealed record ModMoveResult(
    ModMoveOutcome Outcome,
    string Message,
    string? NewFullPath = null);
