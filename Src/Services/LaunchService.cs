using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace STS2ModManager.Services;

/// <summary>
/// Pure-logic helpers for resolving the Slay the Spire 2 install directory
/// and launching the game (directly or via Steam). No WinForms references.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LaunchService
{
    public const string GameExecutableName = "SlayTheSpire2.exe";
    public const string SteamExecutableName = "steam.exe";
    public const int SlayTheSpire2AppId = 2868840;

    public static string ResolveGameDirectory(string? preferredPath)
    {
        if (TryNormalizeGameDirectoryPath(preferredPath, out var normalizedGameDirectory))
        {
            return normalizedGameDirectory;
        }

        return FindGameDirectory(AppContext.BaseDirectory);
    }

    public static bool TryNormalizeGameDirectoryPath(string? candidatePath, out string normalizedGameDirectory)
    {
        normalizedGameDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var trimmedPath = candidatePath.Trim().Trim('"');
        if (trimmedPath.EndsWith(GameExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            trimmedPath = Path.GetDirectoryName(trimmedPath) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return false;
        }

        try
        {
            trimmedPath = Path.GetFullPath(trimmedPath);
        }
        catch
        {
            return false;
        }

        if (!ContainsGameExecutable(trimmedPath))
        {
            return false;
        }

        normalizedGameDirectory = trimmedPath;
        return true;
    }

    public static string FindGameDirectory(string startingDirectory)
    {
        foreach (var candidate in EnumerateGameDirectoryCandidates(startingDirectory))
        {
            if (ContainsGameExecutable(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Could not find {GameExecutableName}. Checked parent directories, Steam libraries, and common install paths.");
    }

    public static bool ContainsGameExecutable(string directoryPath)
    {
        try
        {
            return File.Exists(Path.Combine(directoryPath, GameExecutableName));
        }
        catch
        {
            return false;
        }
    }

    public static string? TryFindSteamPath()
    {
        foreach (var registryLocation in new[]
        {
            (RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
        })
        {
            foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(registryLocation.Item1, registryView);
                    using var steamKey = baseKey.OpenSubKey(registryLocation.Item2);
                    if (steamKey is null)
                    {
                        continue;
                    }

                    foreach (var valueName in new[] { "SteamPath", "InstallPath" })
                    {
                        if (steamKey.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                        {
                            return value.Replace('/', Path.DirectorySeparatorChar).Trim();
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    public static string? TryFindSteamExecutablePath()
    {
        var steamPath = TryFindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return null;
        }

        if (steamPath.EndsWith(SteamExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return steamPath;
        }

        return Path.Combine(steamPath, SteamExecutableName);
    }

    public static void ForceStopGame()
    {
        var processName = Path.GetFileNameWithoutExtension(GameExecutableName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Launches the game using the configured mode. Caller decides how to surface errors.
    /// </summary>
    /// <param name="missingExecutableMessageFactory">
    /// Called when the direct-launch executable is missing. Receives the resolved executable path
    /// and returns a (possibly localized) error message.
    /// </param>
    /// <param name="steamMissingMessage">
    /// Used as the exception message when launching via Steam but Steam is not installed.
    /// </param>
    public static void Launch(
        LaunchMode mode,
        string gameDirectory,
        string launchArguments,
        Func<string, string> missingExecutableMessageFactory,
        string steamMissingMessage)
    {
        if (mode == LaunchMode.Direct)
        {
            LaunchDirectly(gameDirectory, launchArguments, missingExecutableMessageFactory);
            return;
        }

        LaunchViaSteam(launchArguments, steamMissingMessage);
    }

    public static void LaunchDirectly(string gameDirectory, string launchArguments, Func<string, string> missingExecutableMessageFactory)
    {
        var gameExecutablePath = Path.Combine(gameDirectory, GameExecutableName);
        if (!File.Exists(gameExecutablePath))
        {
            throw new InvalidOperationException(missingExecutableMessageFactory(gameExecutablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gameExecutablePath,
            Arguments = launchArguments,
            UseShellExecute = true,
            WorkingDirectory = gameDirectory,
        };

        Process.Start(startInfo);
    }

    public static void LaunchViaSteam(string launchArguments, string steamMissingMessage)
    {
        var steamPath = TryFindSteamExecutablePath();
        if (string.IsNullOrWhiteSpace(steamPath) || !File.Exists(steamPath))
        {
            throw new InvalidOperationException(steamMissingMessage);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = steamPath,
            Arguments = BuildSteamLaunchArguments(launchArguments),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(steamPath) ?? AppContext.BaseDirectory,
        };

        Process.Start(startInfo);
    }

    public static string BuildSteamLaunchArguments(string launchArguments)
    {
        if (string.IsNullOrWhiteSpace(launchArguments))
        {
            return $"-applaunch {SlayTheSpire2AppId}";
        }

        return $"-applaunch {SlayTheSpire2AppId} {launchArguments.Trim()}";
    }

    private static IEnumerable<string> EnumerateGameDirectoryCandidates(string startingDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateParentDirectories(startingDirectory))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var steamPath = TryFindSteamPath();
        if (!string.IsNullOrEmpty(steamPath))
        {
            var defaultLibraryCandidate = Path.Combine(steamPath, "steamapps", "common", "Slay the Spire 2");
            if (seen.Add(defaultLibraryCandidate))
            {
                yield return defaultLibraryCandidate;
            }

            foreach (var candidate in EnumerateSteamLibraryCandidates(steamPath))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var candidate in EnumerateCommonInstallPathCandidates())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateParentDirectories(string startingDirectory)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(startingDirectory));
        while (currentDirectory is not null)
        {
            yield return currentDirectory.FullName;
            currentDirectory = currentDirectory.Parent;
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraryCandidates(string steamPath)
    {
        foreach (var vdfPath in new[]
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf"),
        })
        {
            string content;
            try
            {
                if (!File.Exists(vdfPath))
                {
                    continue;
                }

                content = File.ReadAllText(vdfPath);
            }
            catch
            {
                continue;
            }

            foreach (Match match in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
            {
                if (!match.Success)
                {
                    continue;
                }

                var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    continue;
                }

                yield return Path.Combine(libraryPath, "steamapps", "common", "Slay the Spire 2");
            }

            yield break;
        }
    }

    private static IEnumerable<string> EnumerateCommonInstallPathCandidates()
    {
        var relativePaths = new[]
        {
            Path.Combine("SteamLibrary", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Program Files (x86)", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Program Files", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Games", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Games", "SteamLibrary", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Game", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Game", "SteamLibrary", "steamapps", "common", "Slay the Spire 2"),
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            foreach (var relativePath in relativePaths)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, relativePath);
            }
        }
    }
}
