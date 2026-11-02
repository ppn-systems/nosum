// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Labels;

/// <summary>
/// The labels already drawn in one frame, kept so a label of the same text does not stack on one placed this frame (docs/adr/ADR-091 for hazards,
/// docs/adr/ADR-109 for elite badges). It is a fixed array, so a frame allocates nothing. Labels past <see cref="Capacity"/> are placed as before: the
/// rule only thins clusters, it never drops a label that has no neighbour. Call <see cref="Reset"/> at the start of each frame.
/// </summary>
public sealed class PlacedLabelSet
{
    /// <summary>The number of labels the set remembers in one frame.</summary>
    public const int Capacity = 64;

    private readonly (int LabelId, int X, int Y)[] _placed = new (int, int, int)[Capacity];
    private int _count;

    /// <summary>Forgets every label placed so far; call at the start of each frame.</summary>
    public void Reset() => _count = 0;

    /// <summary>
    /// Places a label unless one of the same text already sits on it (<see cref="HazardLabelRules.Clashes"/>), and records it when it is placed.
    /// </summary>
    /// <param name="labelId">The label's text id.</param>
    /// <param name="x">Anchor x in world pixels.</param>
    /// <param name="y">Anchor y in world pixels.</param>
    /// <returns><see langword="true"/> when the label should be drawn.</returns>
    public bool Take(int labelId, int x, int y)
    {
        for (int i = 0; i < _count; i++)
        {
            (int placedId, int placedX, int placedY) = _placed[i];
            if (HazardLabelRules.Clashes(labelId, x, y, placedId, placedX, placedY))
            {
                return false;
            }
        }

        if (_count < _placed.Length)
        {
            _placed[_count++] = (labelId, x, y);
        }

        return true;
    }
}
