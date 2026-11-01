// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Minimap;

/// <summary>
/// The minimap frame in the top-right HUD. The map itself is drawn on the game canvas (MapScene.Minimap); this view model tells the frame whether
/// the canvas shows the map (the M key, and hidden by the player's setting or by default on a phone), so the frame and the map appear and disappear together.
/// </summary>
public sealed class MinimapViewModel : PanelViewModel
{
    private bool _shown;
    private bool _covered;
    private bool _hidden;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    public MinimapViewModel(IStringLocalizer<Language> text)
        : base(text)
    {
    }

    /// <summary>Gets or sets whether the game draws the minimap. The map scene sets it; <see cref="PanelViewModel.Changed"/> is raised on a change only.</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value)
            {
                return;
            }

            _shown = value;
            this.Raise();
        }
    }

    /// <summary>Gets or sets whether the player hides the minimap (Settings &gt; Display, or hidden by default on a phone). Hidden, the card and the map stay away whatever the M key says.</summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            if (_hidden == value)
            {
                return;
            }

            _hidden = value;
            this.Raise();
        }
    }

    /// <summary>Gets or sets whether a panel covers the HUD's top-right corner; the frame and the map hide meanwhile, and show again after it closes.</summary>
    public bool Covered
    {
        get => _covered;
        set
        {
            if (_covered == value)
            {
                return;
            }

            _covered = value;
            this.Raise();
        }
    }

    /// <summary>Gets whether the frame and the map are drawn now: shown by the player's choice, not hidden in settings, and not covered by a panel.</summary>
    public bool Drawn => _shown && !_hidden && !_covered;

    /// <summary>Gets the localized tooltip of the frame: what a click does.</summary>
    public string Tip => this.Text("Minimap_Tip");
}
