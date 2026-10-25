// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Nodes;

/// <summary>Constants of the node system.</summary>
public static class NodeLimits
{
    /// <summary>Side of a chunk in tiles (docs/adr/ADR-022 D1).</summary>
    public const int ChunkSize = 16;

    /// <summary>Most nodes one instance may hold; a snapshot of a full instance must fit one packet.</summary>
    public const int MaxPerInstance = 1500;

    /// <summary>Capacity of the watering can (docs/adr/ADR-021 D2).</summary>
    public const int WateringCanCapacity = 40;

    /// <summary>Time between two tool swings of one player, milliseconds (docs/adr/ADR-021 D8).</summary>
    public const int SwingCooldownMs = 300;

    /// <summary>Most debris nodes that regrow on one public instance at one game-day rollover (docs/adr/ADR-021 D4).</summary>
    public const int RegrowPerDay = 12;

    /// <summary>Most forage nodes that appear on one public instance at one game-day rollover (docs/adr/ADR-025 D4).</summary>
    public const int ForagePerDay = 6;

    /// <summary>Most forage nodes standing on one public instance at once; unpicked forage is replaced at a season change.</summary>
    public const int MaxForageStanding = 24;

    /// <summary>Game days a tree spends in each of its first three stages (seed, sapling, bush); a grown tree stops growing.</summary>
    public const int DaysPerTreeStage = 2;
}
