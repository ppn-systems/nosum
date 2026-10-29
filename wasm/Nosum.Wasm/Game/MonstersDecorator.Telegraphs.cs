// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Combat;

namespace Nosum.Wasm.Game;

/// <summary>Ground slam telegraphs: the struck area of a boss heavy attack, shown until its impact (docs/adr/ADR-047).</summary>
internal sealed partial class MonstersDecorator
{
    private readonly List<GroundTelegraphClientRow> _telegraphScratch = new(8);

    private void DrawGroundTelegraphs(WorldSpriteBatch world, long now)
    {
        _combat.CopyGroundTelegraphs(_telegraphScratch);
        for (int i = 0; i < _telegraphScratch.Count; i++)
        {
            GroundTelegraphClientRow t = _telegraphScratch[i];
            long elapsed = now - t.StartMs;
            if (elapsed < 0 || elapsed >= t.DelayMs)
            {
                continue;
            }

            int side = ((t.RadiusTiles * 2) + 1) * FrameSize;
            int worldX = (t.X - t.RadiusTiles) * FrameSize;
            int worldY = (t.Y - t.RadiusTiles) * FrameSize;
            if (!this.IsVisible(worldX, worldY, side, side))
            {
                continue;
            }

            // The warning turns more opaque as the impact approaches (0xRRGGBBAA).
            int alpha = 60 + (int)((elapsed * 120L) / t.DelayMs);
            int warningTint = unchecked((int)0xdc262600) | alpha;
            world.Add(
                RenderCommandBuffer.FillTexture,
                0,
                0,
                side,
                side,
                worldX,
                worldY,
                worldY,
                worldX,
                8380,
                tint: warningTint);
        }
    }
}
