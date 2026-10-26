// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.MapGen.Sheets;

/// <summary>A colour of three bytes.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
internal readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>A material's three tones: the shadow, the base and the light.</summary>
/// <param name="Shade">The tone in shadow, bottom and right.</param>
/// <param name="Base">The tone of the surface.</param>
/// <param name="Light">The tone catching the light, top and left.</param>
internal readonly record struct Tones(Rgb Shade, Rgb Base, Rgb Light);

/// <summary>
/// The palette of Nosum's drawn sheets (docs/adr/ADR-042 P2): the tones of the own painted maps, the same values as <c>Material.cs</c> in
/// <c>Nosum.MapReauthor</c>, and the shared dark outline. Light comes from the top left everywhere.
/// </summary>
internal static class Palette
{
    /// <summary>The outline of every object: a dark tone, never black.</summary>
    public static readonly Rgb Outline = new(63, 38, 49);

    /// <summary>Green leaves: grass, bushes and hedges.</summary>
    public static readonly Tones Leaf = new(new Rgb(38, 92, 66), new Rgb(62, 137, 72), new Rgb(99, 199, 77));

    /// <summary>Dark conifer green.</summary>
    public static readonly Tones LeafDark = new(new Rgb(24, 70, 60), new Rgb(38, 110, 72), new Rgb(70, 165, 80));

    /// <summary>Pink blossom.</summary>
    public static readonly Tones Blossom = new(new Rgb(200, 110, 160), new Rgb(240, 160, 200), new Rgb(252, 210, 230));

    /// <summary>Wood and bark.</summary>
    public static readonly Tones Wood = new(new Rgb(119, 56, 51), new Rgb(150, 84, 60), new Rgb(189, 108, 74));

    /// <summary>Brown rock and cliff faces.</summary>
    public static readonly Tones Rock = new(new Rgb(78, 49, 46), new Rgb(122, 87, 61), new Rgb(165, 135, 85));

    /// <summary>Grey stone.</summary>
    public static readonly Tones Stone = new(new Rgb(82, 96, 124), new Rgb(140, 156, 181), new Rgb(192, 203, 220));

    /// <summary>Red roof shingles.</summary>
    public static readonly Tones RoofRed = new(new Rgb(156, 48, 52), new Rgb(214, 72, 56), new Rgb(240, 118, 84));

    /// <summary>Warm plaster walls.</summary>
    public static readonly Tones Plaster = new(new Rgb(189, 108, 74), new Rgb(222, 150, 98), new Rgb(240, 184, 126));

    /// <summary>Brick.</summary>
    public static readonly Tones Brick = new(new Rgb(112, 50, 50), new Rgb(156, 76, 66), new Rgb(190, 106, 84));

    /// <summary>Clear water.</summary>
    public static readonly Tones Water = new(new Rgb(0, 100, 160), new Rgb(46, 177, 225), new Rgb(118, 228, 255));

    /// <summary>Deep water.</summary>
    public static readonly Tones DeepWater = new(new Rgb(0, 60, 110), new Rgb(0, 100, 160), new Rgb(46, 150, 200));

    /// <summary>Sand, straw and hay.</summary>
    public static readonly Tones Sand = new(new Rgb(222, 184, 86), new Rgb(255, 224, 120), new Rgb(255, 240, 170));

    /// <summary>Earth.</summary>
    public static readonly Tones Dirt = new(new Rgb(122, 96, 58), new Rgb(154, 125, 78), new Rgb(179, 149, 95));

    /// <summary>White: paper, eggs, bones, linen.</summary>
    public static readonly Tones White = new(new Rgb(200, 205, 215), new Rgb(240, 230, 200), new Rgb(255, 255, 250));

    /// <summary>Near black: the cave's darkness.</summary>
    public static readonly Tones Dark = new(new Rgb(20, 14, 28), new Rgb(30, 22, 40), new Rgb(46, 36, 58));

    /// <summary>Red: apples, berries, blankets.</summary>
    public static readonly Tones Red = new(new Rgb(160, 40, 44), new Rgb(233, 67, 52), new Rgb(250, 120, 96));

    /// <summary>Orange accent: lamps and flames.</summary>
    public static readonly Tones Yellow = new(new Rgb(214, 140, 30), new Rgb(254, 174, 52), new Rgb(255, 224, 120));

    /// <summary>Purple: crystals and mushrooms.</summary>
    public static readonly Tones Purple = new(new Rgb(96, 64, 150), new Rgb(140, 96, 200), new Rgb(180, 144, 232));

    /// <summary>Cobblestone of the paths.</summary>
    public static readonly Tones Cobble = new(new Rgb(132, 120, 116), new Rgb(168, 156, 144), new Rgb(198, 186, 170));

    /// <summary>The joints between cobblestones.</summary>
    public static readonly Rgb Mortar = new(88, 78, 80);

    /// <summary>The mine floor's stone.</summary>
    public static readonly Tones CaveFloor = new(new Rgb(58, 52, 70), new Rgb(84, 78, 98), new Rgb(112, 106, 126));

    /// <summary>The mine's rock wall.</summary>
    public static readonly Tones CaveWall = new(new Rgb(30, 22, 40), new Rgb(46, 36, 58), new Rgb(76, 64, 92));
}
