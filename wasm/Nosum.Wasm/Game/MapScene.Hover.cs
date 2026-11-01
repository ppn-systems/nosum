// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.Game;

// The crop under the mouse names its growth stage (backlog item 4). The tile comes from the pointer and the camera the way the fishing cast reads it.
internal sealed partial class MapScene
{
    // Reports the tile under the pointer to the actions; a pointer off the canvas reports none. Allocation-free.
    private void HoverAtPointer(int tileSize, int scale, int canvasWidth, int canvasHeight)
    {
        if (_actions is null)
        {
            return;
        }

        if (_pointerX < 0 || _pointerY < 0 || _pointerX >= canvasWidth || _pointerY >= canvasHeight)
        {
            _actions.HoverTile(-1, -1);
            return;
        }

        double worldX = _camera.Left + ((double)_pointerX / scale);
        double worldY = _camera.Top + ((double)_pointerY / scale);
        _actions.HoverTile((int)Math.Floor(worldX / tileSize), (int)Math.Floor(worldY / tileSize));
    }
}
