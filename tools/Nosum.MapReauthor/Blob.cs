// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.MapReauthor;

/// <summary>
/// The 8-neighbour mask of a rounded edge piece. A bit is set when that neighbour is "inside" (the same terrain or one drawn over it, so the
/// shape continues there). Bits: north 1, north-east 2, east 4, south-east 8, south 16, south-west 32, west 64, north-west 128.
/// A corner bit only matters when both of its sides are inside, so a mask is first reduced to its canonical form; 47 canonical masks exist.
/// </summary>
internal static class Blob
{
    /// <summary>North.</summary>
    public const int N = 1;

    /// <summary>North-east.</summary>
    public const int NE = 2;

    /// <summary>East.</summary>
    public const int E = 4;

    /// <summary>South-east.</summary>
    public const int SE = 8;

    /// <summary>South.</summary>
    public const int S = 16;

    /// <summary>South-west.</summary>
    public const int SW = 32;

    /// <summary>West.</summary>
    public const int W = 64;

    /// <summary>North-west.</summary>
    public const int NW = 128;

    /// <summary>The mask of a tile surrounded by its own terrain.</summary>
    public const int Full = 255;

    private static readonly int[] s_canonical;
    private static readonly int[] s_indexOf;

    static Blob()
    {
        s_indexOf = new int[256];
        System.Array.Fill(s_indexOf, -1);
        System.Collections.Generic.List<int> list = [];
        for (int m = 0; m < 256; m++)
        {
            int c = Canonical(m);
            if (s_indexOf[c] < 0)
            {
                s_indexOf[c] = 0;
                list.Add(c);
            }
        }

        list.Sort();
        for (int i = 0; i < list.Count; i++)
        {
            s_indexOf[list[i]] = i;
        }

        s_canonical = [.. list];
    }

    /// <summary>Gets the number of distinct canonical masks.</summary>
    public static int Count => s_canonical.Length;

    /// <summary>Reduces a mask: a corner whose side is outside is cleared.</summary>
    /// <param name="mask">Any 8-bit mask.</param>
    /// <returns>The canonical mask.</returns>
    public static int Canonical(int mask)
    {
        int m = mask & 0xFF;
        if ((m & (N | E)) != (N | E))
        {
            m &= ~NE;
        }

        if ((m & (E | S)) != (E | S))
        {
            m &= ~SE;
        }

        if ((m & (S | W)) != (S | W))
        {
            m &= ~SW;
        }

        if ((m & (W | N)) != (W | N))
        {
            m &= ~NW;
        }

        return m;
    }

    /// <summary>Returns the index of a mask among the 47 canonical ones, ascending by value.</summary>
    /// <param name="mask">Any 8-bit mask.</param>
    /// <returns>The index, 0 to <see cref="Count"/> - 1.</returns>
    public static int IndexOf(int mask) => s_indexOf[Canonical(mask)];

    /// <summary>Returns the canonical mask at an index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The canonical mask.</returns>
    public static int MaskAt(int index) => s_canonical[index];
}
