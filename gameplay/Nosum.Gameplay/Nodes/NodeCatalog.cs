// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Nodes;

/// <summary>
/// The resource-node catalogue (docs/adr/ADR-021 D3 and D5): hits to break, tool, blocking and yields per kind. Hits: twig 1, stone 1, weed 1,
/// grass 1, stump 2, grown tree 10, copper ore 3, iron ore 4. A ladder is not broken: facing it takes a player down. A tree younger than <see cref="TreeStage.Grown"/> falls to one hit and leaves nothing behind.
/// </summary>
public static class NodeCatalog
{
    private static readonly NodeDefinition[] s_all =
    [
        new(NodeKind.Tree, "tree", ToolKind.Axe, 10, true, [new NodeYield(ItemCatalog.Wood, 6, 10)]),
        new(NodeKind.Stump, "stump", ToolKind.Axe, 2, true, [new NodeYield(ItemCatalog.Wood, 2, 4)]),
        new(NodeKind.Twig, "twig", ToolKind.Axe, 1, true, [new NodeYield(ItemCatalog.Wood, 1, 1)]),
        new(NodeKind.Stone, "stone", ToolKind.Pickaxe, 1, true, [new NodeYield(ItemCatalog.Stone, 1, 2), new NodeYield(ItemCatalog.Coal, 1, 1, 50)]),
        new(NodeKind.CopperOre, "copper_ore", ToolKind.Pickaxe, 3, true, [new NodeYield(ItemCatalog.CopperOre, 1, 3)]),
        new(NodeKind.IronOre, "iron_ore", ToolKind.Pickaxe, 4, true, [new NodeYield(ItemCatalog.IronOre, 1, 3)]),
        new(NodeKind.Ladder, "ladder", ToolKind.None, 1, true, []),
        new(NodeKind.Weed, "weed", ToolKind.Scythe, 1, false, [new NodeYield(ItemCatalog.Fiber, 1, 1)]),
        new(NodeKind.Grass, "grass", ToolKind.Scythe, 1, false, [new NodeYield(ItemCatalog.Fiber, 1, 1, 400)]),
    ];

    // A grown tree also drops a seed of its own species now and then; an immature one gives less, see YieldFor.
    private const int SeedChancePermille = 400;

    private static readonly Dictionary<NodeKind, NodeDefinition> s_byKind = Index();

    /// <summary>Gets every node definition.</summary>
    public static IReadOnlyList<NodeDefinition> All => s_all;

    /// <summary>Looks up a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="definition">The definition when the kind is known.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public static bool TryGet(NodeKind kind, [NotNullWhen(true)] out NodeDefinition? definition) => s_byKind.TryGetValue(kind, out definition);

    /// <summary>Returns the hits a freshly spawned node of a kind and stage has.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="stage">The tree stage; ignored for other kinds.</param>
    /// <returns>The hits; zero for an unknown kind.</returns>
    public static byte HitsFor(NodeKind kind, byte stage)
    {
        if (!s_byKind.TryGetValue(kind, out NodeDefinition? definition))
        {
            return 0;
        }

        return kind == NodeKind.Tree && stage < TreeStage.Grown ? (byte)1 : (byte)definition.Hits;
    }

    /// <summary>Returns the item a tree of a species drops as a seed.</summary>
    /// <param name="variant">The species.</param>
    /// <returns>The tree-seed item id.</returns>
    public static int SeedItemFor(byte variant) => variant switch
    {
        TreeVariant.Maple => ItemCatalog.MapleSeed,
        TreeVariant.Pine => ItemCatalog.PineSeed,
        _ => ItemCatalog.OakSeed,
    };

    /// <summary>Returns what a node gives when it breaks, as chances; the roll itself is <see cref="NodeRules.RollYield"/>.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The possible yields.</returns>
    public static IReadOnlyList<NodeYield> YieldFor(in ResourceNode node)
    {
        if (node.Kind != NodeKind.Tree)
        {
            return s_byKind.TryGetValue(node.Kind, out NodeDefinition? definition) ? definition.Yield : [];
        }

        int seed = SeedItemFor(node.Variant);
        return node.Stage switch
        {
            TreeStage.Seed => [new NodeYield(seed, 1, 1)],
            TreeStage.Sapling => [new NodeYield(ItemCatalog.Wood, 1, 1)],
            TreeStage.Bush => [new NodeYield(ItemCatalog.Wood, 2, 3), new NodeYield(seed, 1, 1, SeedChancePermille / 2)],
            _ => [new NodeYield(ItemCatalog.Wood, 6, 10), new NodeYield(seed, 1, 2, SeedChancePermille)],
        };
    }

    private static Dictionary<NodeKind, NodeDefinition> Index()
    {
        Dictionary<NodeKind, NodeDefinition> index = new(s_all.Length);
        foreach (NodeDefinition definition in s_all)
        {
            index.Add(definition.Kind, definition);
        }

        return index;
    }
}
