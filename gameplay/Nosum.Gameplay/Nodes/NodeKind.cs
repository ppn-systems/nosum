// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Nodes;

/// <summary>What a resource node is. The numbers are wire and database values (<c>NodeRow.Kind</c>); append-only.</summary>
public enum NodeKind : byte
{
    /// <summary>No node.</summary>
    None = 0,

    /// <summary>A tree; <see cref="ResourceNode.Variant"/> is the species and <see cref="ResourceNode.Stage"/> how grown it is.</summary>
    Tree = 1,

    /// <summary>What is left of a chopped tree.</summary>
    Stump = 2,

    /// <summary>A fallen branch.</summary>
    Twig = 3,

    /// <summary>A stone.</summary>
    Stone = 4,

    /// <summary>A copper ore node.</summary>
    CopperOre = 5,

    /// <summary>A weed; never blocks walking.</summary>
    Weed = 6,

    /// <summary>Tall grass; never blocks walking.</summary>
    Grass = 7,

    /// <summary>An iron ore node, found on the deeper floors of the mine (docs/adr/ADR-026 D3).</summary>
    IronOre = 9,

    /// <summary>A ladder down to the next mine floor, revealed when the stone that hid it breaks; used by facing it (docs/adr/ADR-026 D3).</summary>
    Ladder = 10,
}

/// <summary>The values of <see cref="ResourceNode.Variant"/> for kinds that are not trees.</summary>
public static class NodeVariants
{
    /// <summary>A stone on a mine floor that hides the ladder: when it breaks, a <see cref="NodeKind.Ladder"/> stands in its place.</summary>
    public const byte LadderStone = 1;
}

/// <summary>Tree species, the values of <see cref="ResourceNode.Variant"/>. They are also the art: oak is <c>tree1</c>, maple <c>tree2</c>, pine <c>tree3</c> in the placeholder sheets.</summary>
public static class TreeVariant
{
    /// <summary>An oak.</summary>
    public const byte Oak = 0;

    /// <summary>A maple.</summary>
    public const byte Maple = 1;

    /// <summary>A pine.</summary>
    public const byte Pine = 2;

    /// <summary>The number of species.</summary>
    public const int Count = 3;
}

/// <summary>How grown a tree is, the values of <see cref="ResourceNode.Stage"/>.</summary>
public static class TreeStage
{
    /// <summary>A planted seed.</summary>
    public const byte Seed = 0;

    /// <summary>A sapling.</summary>
    public const byte Sapling = 1;

    /// <summary>A bush.</summary>
    public const byte Bush = 2;

    /// <summary>A full tree: it takes ten axe hits and falls to a stump.</summary>
    public const byte Grown = 3;
}
