using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2ModManager.Services.UI;

/// <summary>
/// Loads localization tables from embedded JSON resources, with optional on-disk
/// per-language overrides under <c>{AppContext.BaseDirectory}/localization/{lang}/strings.json</c>.
/// English (<c>eng</c>) is always the fallback for missing keys.
/// </summary>
internal sealed class LocalizationService
{
    private const string FallbackLanguageCode = "eng";

    private readonly Dictionary<string, string> fallback;
    private Dictionary<string, string> active;
    private readonly HashSet<string> warnedMissing = new(StringComparer.Ordinal);

    public string LanguageCode { get; private set; }

    public event Action? LanguageChanged;

    public LocalizationService(string initialLanguageCode)
    {
        fallback = LoadCombined(FallbackLanguageCode);
        LanguageCode = NormalizeCode(initialLanguageCode);
        active = string.Equals(LanguageCode, FallbackLanguageCode, StringComparison.Ordinal)
            ? fallback
            : LoadCombined(LanguageCode);
    }

    public void SetLanguage(string languageCode)
    {
        var normalized = NormalizeCode(languageCode);
        if (string.Equals(normalized, LanguageCode, StringComparison.Ordinal))
        {
            return;
        }

        LanguageCode = normalized;
        active = string.Equals(LanguageCode, FallbackLanguageCode, StringComparison.Ordinal)
            ? fallback
            : LoadCombined(LanguageCode);
        warnedMissing.Clear();
        LanguageChanged?.Invoke();
    }

    public string Get(string key)
    {
        if (active.TryGetValue(key, out var value))
        {
            return value;
        }

        if (fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        WarnMissing(key);
        return key;
    }

    public string Get(string key, params object?[] args)
    {
        var template = Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            WarnMissing(key + " (format)");
            return template;
        }
    }

    private void WarnMissing(string key)
    {
        if (warnedMissing.Add(key))
        {
            Debug.WriteLine($"[i18n] Missing key '{key}' for language '{LanguageCode}'.");
        }
    }

    private static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return FallbackLanguageCode;
        }

        return code.Trim().ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "eng",
            "zh" or "zhs" or "zh-cn" or "zh_cn" or "chs" or "chinese" => "zhs",
            var other => other,
        };
    }

    private static Dictionary<string, string> LoadCombined(string languageCode)
    {
        var combined = LoadEmbedded(languageCode);
        OverlayFromDisk(combined, languageCode);
        return combined;
    }

    private static Dictionary<string, string> LoadEmbedded(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = $"STS2ModManager.localization.{languageCode}.strings.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return DeserializeJson(stream) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void OverlayFromDisk(Dictionary<string, string> target, string languageCode)
    {
        try
        {
            var diskPath = Path.Combine(AppContext.BaseDirectory, "localization", languageCode, "strings.json");
            if (!File.Exists(diskPath))
            {
                return;
            }

            using var stream = File.OpenRead(diskPath);
            var overlay = DeserializeJson(stream);
            if (overlay is null)
            {
                return;
            }

            foreach (var pair in overlay)
            {
                target[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[i18n] Failed to load on-disk overrides for '{languageCode}': {ex.Message}");
        }
    }

    private static Dictionary<string, string>? DeserializeJson(Stream stream)
    {
        return JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.DictionaryStringString);
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJsonContext : JsonSerializerContext
{
}
