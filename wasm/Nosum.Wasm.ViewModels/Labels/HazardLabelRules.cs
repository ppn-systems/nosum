// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Labels;

/// <summary>
/// The rule that keeps a field of hazards from stacking one label over another (docs/adr/ADR-091). A magma field places a fissure every few tiles,
/// and each drew its own "Magma fissure" label, so the same words sat on top of each other in the view. A label is placed only when no label of the
/// same text is within <see cref="SpanX"/> pixels across and <see cref="SpanY"/> pixels down of one already placed this frame. Pure.
/// </summary>
public static class HazardLabelRules
{
    /// <summary>The horizontal distance, in pixels, a same-text label keeps from one already placed: a label is about 90 pixels wide on a 1280 × 720 view.</summary>
    public const int SpanX = 96;

    /// <summary>The vertical distance, in pixels, a same-text label keeps from one already placed: a label line is about 12 pixels tall.</summary>
    public const int SpanY = 16;

    /// <summary>Returns whether a label at (<paramref name="x"/>, <paramref name="y"/>) would sit on top of an already placed one.</summary>
    /// <param name="labelId">The label's text id.</param>
    /// <param name="x">Anchor x in world pixels.</param>
    /// <param name="y">Anchor y in world pixels.</param>
    /// <param name="placedId">The text id of a placed label.</param>
    /// <param name="placedX">The placed label's anchor x.</param>
    /// <param name="placedY">The placed label's anchor y.</param>
    /// <returns><see langword="true"/> when the two would stack.</returns>
    public static bool Clashes(int labelId, int x, int y, int placedId, int placedX, int placedY)
        => labelId == placedId && Math.Abs(x - placedX) < SpanX && Math.Abs(y - placedY) < SpanY;
}
