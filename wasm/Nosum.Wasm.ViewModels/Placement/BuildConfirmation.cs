// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Placement;

/// <summary>
/// The second press that a farm building needs (docs/adr/ADR-028). A finished building cannot be moved or stowed, so the first place press on a
/// building only arms the confirmation; a second press on the same tile within <see cref="WindowSeconds"/> places it. Moving the ghost or waiting
/// past the window disarms it. Pure and allocation-free: the placement actions call <see cref="Press"/> once per place press and <see cref="Disarm"/> when the ghost moves.
/// </summary>
public struct BuildConfirmation
{
    /// <summary>How long the first press stays armed, in seconds.</summary>
    public const double WindowSeconds = 6;

    private int _tileX;
    private int _tileY;
    private double _armedAt;
    private bool _armed;

    /// <summary>Gets whether a first press is waiting for the second one.</summary>
    /// <param name="nowSeconds">The scene clock.</param>
    /// <returns>True while the confirmation is pending.</returns>
    public readonly bool IsArmed(double nowSeconds) => _armed && nowSeconds - _armedAt <= WindowSeconds;

    /// <summary>Handles a place press on a building at a tile.</summary>
    /// <param name="tileX">The left tile of the building's footprint.</param>
    /// <param name="tileY">The top tile of the building's footprint.</param>
    /// <param name="nowSeconds">The scene clock.</param>
    /// <returns>True when this press confirms the build and the placement should be sent; false when it only arms the confirmation.</returns>
    public bool Press(int tileX, int tileY, double nowSeconds)
    {
        if (this.IsArmed(nowSeconds) && _tileX == tileX && _tileY == tileY)
        {
            this.Disarm();
            return true;
        }

        _tileX = tileX;
        _tileY = tileY;
        _armedAt = nowSeconds;
        _armed = true;
        return false;
    }

    /// <summary>Drops a pending confirmation (the ghost moved, or the player left the room).</summary>
    public void Disarm() => _armed = false;
}
