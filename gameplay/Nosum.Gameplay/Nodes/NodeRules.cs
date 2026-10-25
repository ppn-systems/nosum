// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Nodes;

/// <summary>Result of using a tool on a node.</summary>
public enum NodeHitOutcome
{
    /// <summary>The held tool does not work on this node.</summary>
    WrongTool = 0,

    /// <summary>The node took a hit and still stands (<see cref="NodeHit.Next"/> has fewer hits).</summary>
    Damaged = 1,

    /// <summary>The node broke: it gives its yield and is replaced by <see cref="NodeHit.Next"/>, or removed when that is <see langword="null"/>.</summary>
    Broke = 2,
}

/// <summary>What one hit does.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Next">The node after the hit; <see langword="null"/> when the node is gone.</param>
public readonly record struct NodeHit(NodeHitOutcome Outcome, ResourceNode? Next);

/// <summary>The pure node rules: hits, breaking, yields and tree growth. Randomness is a parameter, so a test can pin every outcome.</summary>
public static class NodeRules
{
    /// <summary>Applies one tool swing to a node. A grown tree that breaks becomes a stump of the same id and species; every other node that breaks is removed.</summary>
    /// <param name="node">The node.</param>
    /// <param name="tool">The held tool.</param>
    /// <returns>What happened.</returns>
    public static NodeHit Hit(in ResourceNode node, ToolKind tool) => Hit(node, tool, 1);

    /// <summary>
    /// Applies one tool swing of a given strength (docs/adr/ADR-026 D1: an upgraded axe or pickaxe does more than one hit of damage). A stone
    /// that hides a mine ladder (<see cref="NodeVariants.LadderStone"/>) leaves the ladder behind when it breaks.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="tool">The held tool.</param>
    /// <param name="damage">Hits' worth of damage this swing does, at least one (<see cref="ToolRules.DamagePerHit"/>).</param>
    /// <returns>What happened.</returns>
    public static NodeHit Hit(in ResourceNode node, ToolKind tool, int damage)
    {
        if (!NodeCatalog.TryGet(node.Kind, out NodeDefinition? definition) || definition.Tool != tool || node.Kind == NodeKind.Ladder)
        {
            return new NodeHit(NodeHitOutcome.WrongTool, node);
        }

        if (node.Hits > Math.Max(1, damage))
        {
            return new NodeHit(NodeHitOutcome.Damaged, node with { Hits = (byte)(node.Hits - Math.Max(1, damage)) });
        }

        if (node is { Kind: NodeKind.Stone, Variant: NodeVariants.LadderStone })
        {
            return new NodeHit(NodeHitOutcome.Broke, new ResourceNode(node.Id, node.X, node.Y, NodeKind.Ladder, 0, 0, NodeCatalog.HitsFor(NodeKind.Ladder, 0)));
        }

        if (node is { Kind: NodeKind.Tree, Stage: TreeStage.Grown })
        {
            return new NodeHit(NodeHitOutcome.Broke, node with { Kind = NodeKind.Stump, Stage = 0, Hits = NodeCatalog.HitsFor(NodeKind.Stump, 0) });
        }

        return new NodeHit(NodeHitOutcome.Broke, null);
    }

    /// <summary>Rolls what a breaking node gives.</summary>
    /// <param name="node">The node that broke (before it changed into a stump).</param>
    /// <param name="seed">Seed for the roll; use <see cref="NodeRandom.Mix"/> with the instance seed and the node's id.</param>
    /// <returns>The items; may be empty.</returns>
    public static List<ItemSlot> RollYield(in ResourceNode node, ulong seed)
    {
        NodeRandom random = new(seed);
        List<ItemSlot> items = [];
        foreach (NodeYield yield in NodeCatalog.YieldFor(node))
        {
            if (!random.Chance(yield.ChancePermille))
            {
                continue;
            }

            int quantity = yield.Min + random.Next(yield.Max - yield.Min + 1);
            if (quantity > 0)
            {
                items.Add(new ItemSlot(yield.ItemId, quantity));
            }
        }

        return items;
    }

    /// <summary>Returns the stage a tree has reached after a number of game days.</summary>
    /// <param name="ageDays">Days since it was planted.</param>
    /// <returns>The stage, <see cref="TreeStage.Seed"/> to <see cref="TreeStage.Grown"/>.</returns>
    public static byte StageForAge(int ageDays) => (byte)Math.Min(TreeStage.Grown, Math.Max(0, ageDays) / NodeLimits.DaysPerTreeStage);

    /// <summary>Ages a node by game days; a young tree grows into the stage its age has reached.</summary>
    /// <param name="node">The node.</param>
    /// <param name="days">Days that passed, at least one.</param>
    /// <returns>The aged node; the same node when nothing changed.</returns>
    public static ResourceNode Age(in ResourceNode node, int days)
    {
        if (node.Kind != NodeKind.Tree || node.Stage >= TreeStage.Grown || days <= 0)
        {
            return node;
        }

        int age = node.AgeDays + days;
        byte stage = StageForAge(age);
        return node with { AgeDays = age, Stage = stage, Hits = stage == node.Stage ? node.Hits : NodeCatalog.HitsFor(NodeKind.Tree, stage) };
    }

    /// <summary>Returns the node a planted tree seed becomes.</summary>
    /// <param name="id">The new node's id.</param>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <param name="variant">The species.</param>
    /// <returns>A seed-stage tree.</returns>
    public static ResourceNode PlantedTree(int id, int x, int y, byte variant)
        => new(id, x, y, NodeKind.Tree, variant, TreeStage.Seed, NodeCatalog.HitsFor(NodeKind.Tree, TreeStage.Seed), 0);
}
