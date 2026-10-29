// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Nodes;

/// <summary>One thing a node may give when it breaks. Data, not code: balance changes touch only the catalogue.</summary>
/// <param name="ItemId">The item given.</param>
/// <param name="Min">Fewest given when the roll succeeds.</param>
/// <param name="Max">Most given when the roll succeeds.</param>
/// <param name="ChancePermille">Chance the item drops at all, out of 1000.</param>
public readonly record struct NodeYield(int ItemId, int Min, int Max, int ChancePermille = 1000);

/// <summary>What a kind of node is: how many hits it takes, which tool breaks it, whether it blocks walking, and what it gives.</summary>
public sealed class NodeDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="key">Stable key for localization (<c>node.&lt;key&gt;.name</c>).</param>
    /// <param name="tool">The tool that works on it.</param>
    /// <param name="hits">Hits to break it.</param>
    /// <param name="blocks">Whether it blocks walking.</param>
    /// <param name="yield">What it gives when broken.</param>
    public NodeDefinition(NodeKind kind, string key, ToolKind tool, int hits, bool blocks, IReadOnlyList<NodeYield> yield)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hits);
        ArgumentNullException.ThrowIfNull(yield);

        this.Kind = kind;
        this.Key = key;
        this.Tool = tool;
        this.Hits = hits;
        this.Blocks = blocks;
        this.Yield = yield;
    }

    /// <summary>Gets the kind.</summary>
    public NodeKind Kind { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the tool that works on the node.</summary>
    public ToolKind Tool { get; }

    /// <summary>Gets the hits it takes to break the node (a grown tree; younger trees fall in one, see <see cref="NodeCatalog.HitsFor"/>).</summary>
    public int Hits { get; }

    /// <summary>Gets whether the node blocks walking.</summary>
    public bool Blocks { get; }

    /// <summary>Gets what the node gives when it breaks.</summary>
    public IReadOnlyList<NodeYield> Yield { get; }

    /// <summary>Gets the lowest tool tier that breaks the node (docs/adr/ADR-026 D1): a tool of a lower tier does nothing to it. Basic for anything a starter tool can break.</summary>
    public ToolTier RequiredTier { get; init; } = ToolTier.Basic;
}
