// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>One farmer on the map this frame, as the decorators that follow farmers see it.</summary>
/// <param name="Id">The player's id on the map (zero before joining).</param>
/// <param name="X">Feet x, world pixels.</param>
/// <param name="Y">Feet y, world pixels.</param>
/// <param name="Facing">Facing, 0 down, 1 right, 2 up, 3 left.</param>
/// <param name="Moving">Whether the farmer walked this frame.</param>
/// <param name="Local">Whether this is the player's own farmer.</param>
internal readonly record struct FarmerEntry(uint Id, float X, float Y, byte Facing, bool Moving, bool Local);

/// <summary>
/// The farmers on the map this frame: the local farmer and the remote ones in sight. <see cref="MapScene"/> fills it before the decorators add
/// their sprites, so a pet follows and an emote bubble sits above the same position the farmer is drawn at. A fixed array reused every frame:
/// no allocation on the frame path. Read on the frame thread only.
/// </summary>
internal sealed class FarmerFeed
{
    /// <summary>Most farmers kept in one frame: the remote ones plus the local farmer.</summary>
    public const int Capacity = 64;

    private readonly FarmerEntry[] _entries = new FarmerEntry[Capacity];
    private int _count;

    /// <summary>Gets the number of farmers this frame.</summary>
    public int Count => _count;

    /// <summary>Returns a farmer of this frame.</summary>
    /// <param name="index">Index below <see cref="Count"/>.</param>
    /// <returns>The farmer.</returns>
    public FarmerEntry At(int index) => _entries[index];

    /// <summary>Starts a new frame: forgets the farmers of the last one.</summary>
    public void Begin() => _count = 0;

    /// <summary>Adds a farmer to this frame; a farmer beyond the capacity is not kept.</summary>
    /// <param name="entry">The farmer.</param>
    public void Add(FarmerEntry entry)
    {
        if (_count < Capacity)
        {
            _entries[_count++] = entry;
        }
    }
}
