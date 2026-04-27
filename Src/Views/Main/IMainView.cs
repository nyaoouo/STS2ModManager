using System;
using System.Runtime.Versioning;

namespace STS2ModManager.Views.Main;

/// <summary>
/// Top-level window chrome contract. Implemented by the form/window that
/// hosts the three tabbed views (mods, saves, config).
/// </summary>
/// <remarks>
/// Phase 4a introduces this interface but does not wire it up. Phase 4b
/// makes <c>MainForm</c> implement it and routes
/// <see cref="RefreshAllRequested"/> + status updates through
/// <c>MainPresenter</c>. Add new members here as later phases need them
/// rather than letting presenters reach into concrete views.
/// </remarks>
[SupportedOSPlatform("windows")]
internal interface IMainView
{
    /// <summary>Fired when the user activates a top-level page (mods/saves/config tab).</summary>
    event Action<AppPage>? PageChanged;

    /// <summary>Fired when the user requests a global refresh (mods + saves).</summary>
    event Action? RefreshAllRequested;

    /// <summary>Update the status bar text.</summary>
    void SetStatus(string text);

    /// <summary>Make the given page the active tab.</summary>
    void SetActivePage(AppPage page);

    /// <summary>Show the drag-drop overlay (archives being dragged in).</summary>
    void ShowDropOverlay();

    /// <summary>Hide the drag-drop overlay.</summary>
    void HideDropOverlay();
}
