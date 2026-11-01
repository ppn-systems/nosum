// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Placement;

/// <summary>
/// Keeps the ghost off the tile of a piece the player has just placed or moved (docs/design/critique-placement.md, finding 3). The placed piece
/// is drawn by the world on that tile, so a ghost left there would read as a second, red piece. <see cref="Settle"/> is called after a
/// successful place; the ghost stays hidden on that tile and shows again once it is aimed at another tile. Pure and allocation-free.
/// </summary>
public struct SettledGhost
{
    private int _tileX;
    private int _tileY;
    private bool _settled;

    /// <summary>Remembers that a piece was just placed on a tile; the ghost is hidden there from now on.</summary>
    /// <param name="tileX">The left tile of the placed footprint.</param>
    /// <param name="tileY">The top tile of the placed footprint.</param>
    public void Settle(int tileX, int tileY)
    {
        _tileX = tileX;
        _tileY = tileY;
        _settled = true;
    }

    /// <summary>Asks whether the ghost is hidden at a tile this frame. Aiming at any other tile ends the hiding, so it returns and clears in one step.</summary>
    /// <param name="tileX">The left tile of the ghost's footprint this frame.</param>
    /// <param name="tileY">The top tile of the ghost's footprint this frame.</param>
    /// <returns>True while the ghost should not be drawn.</returns>
    public bool Hide(int tileX, int tileY)
    {
        if (!_settled)
        {
            return false;
        }

        if (tileX == _tileX && tileY == _tileY)
        {
            return true;
        }

        _settled = false;
        return false;
    }
}
