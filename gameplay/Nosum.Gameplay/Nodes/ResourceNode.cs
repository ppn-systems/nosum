// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Nodes;

/// <summary>
/// One resource node standing on a tile of a map instance (docs/adr/ADR-021 D3). A value: changing a node means replacing it in the
/// <see cref="NodeField"/>.
/// </summary>
/// <param name="Id">Node id, unique across the game and never reused.</param>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="Kind">What the node is.</param>
/// <param name="Variant">For a tree or stump, the species (<see cref="TreeVariant"/>); zero otherwise.</param>
/// <param name="Stage">For a tree, how grown it is (<see cref="TreeStage"/>); zero otherwise.</param>
/// <param name="Hits">Hits left before the node breaks.</param>
/// <param name="AgeDays">Game days the node has stood; a tree grows by it (<see cref="NodeRules.StageForAge"/>).</param>
public readonly record struct ResourceNode(int Id, int X, int Y, NodeKind Kind, byte Variant, byte Stage, byte Hits, int AgeDays = 0)
{
    /// <summary>
    /// Gets whether the node stops a player walking through its tile. A tree blocks only once it is fully grown: a planted seed, a sapling or a
    /// bush is walked over. The grown tree and its stump block.
    /// </summary>
    public bool Blocks => NodeCatalog.TryGet(this.Kind, out NodeDefinition? definition) && definition.Blocks && (this.Kind != NodeKind.Tree || this.Stage == TreeStage.Grown);
}
