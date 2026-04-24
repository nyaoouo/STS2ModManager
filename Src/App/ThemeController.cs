using System;
using System.Runtime.Versioning;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Win32;

namespace STS2ModManager.App;

/// <summary>
/// Owns the application-wide <see cref="MaterialSkinManager"/> configuration:
/// applies a Material theme + colour scheme to the form, and keeps the theme in
/// sync with the user-selected mode (<see cref="ThemeMode.System"/>,
/// <see cref="ThemeMode.Light"/>, or <see cref="ThemeMode.Dark"/>).
///
/// When in <see cref="ThemeMode.System"/> the controller listens for Windows
/// theme changes and flips Light/Dark accordingly.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ThemeController : IDisposable
{
    private readonly MaterialForm form;
    private readonly MaterialSkinManager skin;
    private ThemeMode mode;
    private bool listeningSystem;
    private bool effectiveDark;

    public event Action? EffectiveThemeChanged;

    public bool IsEffectiveDark => effectiveDark;

    public ThemeController(MaterialForm form)
    {
        this.form = form ?? throw new ArgumentNullException(nameof(form));
        skin = MaterialSkinManager.Instance;
        skin.AddFormToManage(form);
        SetMode(ThemeMode.System);
        form.Disposed += (_, _) => Dispose();
    }

    public ThemeMode Mode => mode;

    /// <summary>
    /// Re-applies the current effective theme. Use after late-added controls
    /// (e.g. <see cref="MaterialTabSelector"/>) so they pick up the right colors.
    /// </summary>
    public void Refresh() => ApplyEffectiveTheme();

    public void SetMode(ThemeMode newMode)
    {
        mode = newMode;
        ApplyEffectiveTheme();
        StartOrStopSystemListener();
    }

    private void ApplyEffectiveTheme()
    {
        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => IsSystemDarkMode(),
        };
        skin.Theme = dark ? MaterialSkinManager.Themes.DARK : MaterialSkinManager.Themes.LIGHT;
        skin.ColorScheme = dark
            ? new ColorScheme(
                Primary.BlueGrey800,
                Primary.BlueGrey900,
                Primary.BlueGrey500,
                Accent.LightBlue200,
                TextShade.WHITE)
            : new ColorScheme(
                Primary.BlueGrey50,
                Primary.BlueGrey100,
                Primary.BlueGrey200,
                Accent.Blue400,
                TextShade.BLACK);
        effectiveDark = dark;
        if (!form.IsDisposed)
        {
            form.Invalidate(invalidateChildren: true);
        }
        EffectiveThemeChanged?.Invoke();
    }

    private void StartOrStopSystemListener()
    {
        var shouldListen = mode == ThemeMode.System;
        if (shouldListen == listeningSystem)
        {
            return;
        }

        if (shouldListen)
        {
            SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
        }

        listeningSystem = shouldListen;
    }

    private void HandleUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (eventArgs.Category != UserPreferenceCategory.General)
        {
            return;
        }

        if (form.IsDisposed)
        {
            return;
        }

        if (form.InvokeRequired)
        {
            form.BeginInvoke(new Action(ApplyEffectiveTheme));
        }
        else
        {
            ApplyEffectiveTheme();
        }
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLight)
            {
                return appsUseLight == 0;
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    public void Dispose()
    {
        if (listeningSystem)
        {
            SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
            listeningSystem = false;
        }
    }
}

internal enum ThemeMode
{
    System,
    Light,
    Dark,
}
