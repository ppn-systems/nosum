// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// What the own maps of the Mountain, the Beach and the Woods do not draw (docs/adr/ADR-037, ADR-042 P5c): the tiles of the Beach's broken bridge, where shells
/// wash up, the large stumps of the Woods and where extra stones, twigs and weeds lie. Tile coordinates, checked against the map files by tests.
/// </summary>
public static class OutdoorWorld
{
    /// <summary>Index on the <c>town</c> sheet of the plank drawn over the Beach bridge's gate (a passable plank of <c>town.tileset</c>, see <c>BridgeDecorator</c>).</summary>
    public const int BridgePlankTile = 16;

    /// <summary>First tile x of the broken span of the Beach bridge (the gap in the river, where the Beach layout runs water).</summary>
    public const int BridgeFromX = 58;

    /// <summary>Last tile x of the broken span of the Beach bridge.</summary>
    public const int BridgeToX = 61;

    /// <summary>Tile row of the Beach bridge.</summary>
    public const int BridgeY = 13;

    /// <summary>Beach tile on the west bank where the bridge starts.</summary>
    public static readonly (int X, int Y) BridgeWestBank = (BridgeFromX - 1, BridgeY);

    /// <summary>Beach tile on the east bank where the bridge ends.</summary>
    public static readonly (int X, int Y) BridgeEastBank = (BridgeToX + 1, BridgeY);

    /// <summary>The Beach bridge: four tiles that stay blocked until the <see cref="CommunityUnlockKeys.BeachBridge"/> unlock is set.</summary>
    public static MapGate BeachBridge { get; } = new(CommunityUnlockKeys.BeachBridge, [(BridgeFromX, BridgeY), (BridgeFromX + 1, BridgeY), (BridgeFromX + 2, BridgeY), (BridgeToX, BridgeY)]);

    /// <summary>The Beach's shore forage: the sand west of the river and east of it (the file marks no forage spot there).</summary>
    public static IReadOnlyList<ForageArea> BeachForage { get; } =
    [
        new(9, 6, 56, 24, ForagePools.Shore),
        new(63, 5, 92, 24, ForagePools.Shore),
    ];

    /// <summary>Driftwood and rocks on the Beach (the file marks none).</summary>
    public static IReadOnlyList<NodeScatter> BeachNodes { get; } =
    [
        new(NodeKind.Twig, 30, 9, 6, 56, 24),
        new(NodeKind.Stone, 20, 9, 6, 56, 24),
        new(NodeKind.Twig, 20, 63, 5, 92, 24),
        new(NodeKind.Stone, 20, 63, 5, 92, 24),
    ];

    /// <summary>
    /// Planks of the Mountain that cross water: the walk grid blocks water of the <c>Back</c> layer whatever stands on it, so the bridge over the stream below the mine
    /// path (four tiles of row 7) is opened by hand, like the mine shaft (docs/adr/ADR-037 D1).
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> MountainBridges { get; } = [(46, 7), (47, 7), (48, 7), (49, 7)];

    /// <summary>Stones, twigs and weeds of the Mountain's reachable parts, west of the stream and on the mine path: the file marks only eight trees and forage spots.</summary>
    public static IReadOnlyList<NodeScatter> MountainNodes { get; } =
    [
        new(NodeKind.Stone, 40, 4, 6, 47, 36),
        new(NodeKind.Twig, 20, 4, 6, 47, 36),
        new(NodeKind.Weed, 40, 4, 6, 47, 36),
        new(NodeKind.Stone, 14, 60, 9, 84, 15),
        new(NodeKind.Twig, 8, 60, 9, 84, 15),
        new(NodeKind.Weed, 20, 60, 9, 84, 15),
    ];

    /// <summary>The six large stumps of the Woods (docs/adr/ADR-037 D2): the file marks no tree or stump there.</summary>
    public static IReadOnlyList<NodeSite> WoodsStumps { get; } =
    [
        new(10, 12, NodeKind.LargeStump, 0),
        new(24, 12, NodeKind.LargeStump, 0),
        new(28, 6, NodeKind.LargeStump, 0),
        new(46, 5, NodeKind.LargeStump, 0),
        new(40, 27, NodeKind.LargeStump, 0),
        new(4, 25, NodeKind.LargeStump, 0),
    ];

    /// <summary>Twigs, weeds and stones of the Woods.</summary>
    public static IReadOnlyList<NodeScatter> WoodsNodes { get; } =
    [
        new(NodeKind.Twig, 30, 2, 4, 55, 30),
        new(NodeKind.Weed, 40, 2, 4, 55, 30),
        new(NodeKind.Stone, 12, 2, 4, 55, 30),
    ];
}
