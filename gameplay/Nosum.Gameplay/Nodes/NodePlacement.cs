// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Nodes;

/// <summary>A node to create: where and what. The database gives it its id.</summary>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Variant">The tree species, for a tree.</param>
/// <param name="Stage">The tree stage, for a tree.</param>
public readonly record struct NodePlacement(int X, int Y, NodeKind Kind, byte Variant, byte Stage);

/// <summary>
/// Decides which nodes an instance gets: the first fill from the map's markers when the instance is created (docs/adr/ADR-021 D4a), and the
/// bounded regrowth at a game-day rollover on public maps (D4b). Both are deterministic from an instance seed, so tests pin them.
/// </summary>
public static class NodePlacements
{
    // Share of marked sites that really get a node, out of 1000. Trees are always placed (they are the landscape); debris is thinned so a
    // farm does not start as a field of obstacles (the Farm map marks several hundred sites per kind).
    private const int TreeChance = 1000;
    private const int WeedChance = 400;
    private const int StoneChance = 350;
    private const int TwigChance = 400;
    private const int GrassChance = 150;

    /// <summary>Chooses the nodes a new instance starts with.</summary>
    /// <param name="sites">The map's markers.</param>
    /// <param name="grid">The map's own walk grid; a marker on a blocked tile is skipped.</param>
    /// <param name="reserved">Tiles that must stay clear (arrival points, exits); markers on or next to them are skipped.</param>
    /// <param name="seed">The instance seed.</param>
    /// <returns>The placements, at most <see cref="NodeLimits.MaxPerInstance"/>.</returns>
    public static List<NodePlacement> Initial(IReadOnlyList<NodeSite> sites, WalkGrid grid, IReadOnlySet<(int X, int Y)> reserved, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(reserved);

        NodeRandom random = new(seed);
        List<NodePlacement> placements = [];
        foreach (NodeSite site in sites)
        {
            // Roll for every site so one skipped tile does not shift the rolls of the rest.
            bool lucky = random.Chance(ChanceOf(site.Kind));
            if (!lucky || grid.IsBlocked(site.X, site.Y) || NearReserved(reserved, site.X, site.Y) || placements.Count >= NodeLimits.MaxPerInstance)
            {
                continue;
            }

            placements.Add(new NodePlacement(site.X, site.Y, site.Kind, site.Variant, site.Kind == NodeKind.Tree ? TreeStage.Grown : (byte)0));
        }

        return placements;
    }

    /// <summary>Chooses the debris that regrows at a game-day rollover.</summary>
    /// <param name="sites">The map's markers: debris only regrows where the map says it belongs.</param>
    /// <param name="field">The nodes standing now.</param>
    /// <param name="grid">The map's own walk grid.</param>
    /// <param name="reserved">Tiles that must stay clear.</param>
    /// <param name="occupied">Tiles a player stands on; nothing grows under a player.</param>
    /// <param name="seed">Seed for this instance and this day.</param>
    /// <param name="isPrivate">Whether the instance is one player's own place: it only regrows weeds, and only when <paramref name="seasonChanged"/>.</param>
    /// <param name="seasonChanged">Whether this rollover starts a new season.</param>
    /// <returns>The placements, at most <see cref="NodeLimits.RegrowPerDay"/>.</returns>
    public static List<NodePlacement> Regrowth(IReadOnlyList<NodeSite> sites, NodeField field, WalkGrid grid, IReadOnlySet<(int X, int Y)> reserved, IReadOnlySet<(int X, int Y)> occupied, ulong seed, bool isPrivate, bool seasonChanged)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(occupied);

        List<NodePlacement> placements = [];
        if (sites.Count == 0 || (isPrivate && !seasonChanged))
        {
            return placements;
        }

        NodeRandom random = new(seed);
        HashSet<(int, int)> taken = [];
        int attempts = NodeLimits.RegrowPerDay * 6;
        while (attempts-- > 0 && placements.Count < NodeLimits.RegrowPerDay && field.Count + placements.Count < NodeLimits.MaxPerInstance)
        {
            NodeSite site = sites[random.Next(sites.Count)];
            bool regrows = isPrivate ? site.Kind == NodeKind.Weed : site.Kind is NodeKind.Weed or NodeKind.Stone or NodeKind.Twig;
            if (!regrows
                || field.HasNodeAt(site.X, site.Y)
                || grid.IsBlocked(site.X, site.Y)
                || occupied.Contains((site.X, site.Y))
                || NearReserved(reserved, site.X, site.Y)
                || !taken.Add((site.X, site.Y)))
            {
                continue;
            }

            placements.Add(new NodePlacement(site.X, site.Y, site.Kind, 0, 0));
        }

        return placements;
    }

    /// <summary>
    /// Chooses the forage that appears at a game-day rollover on a public map (docs/adr/ADR-025 D4): up to <see cref="NodeLimits.ForagePerDay"/> of the
    /// season's items on free forage spots, never more than <see cref="NodeLimits.MaxForageStanding"/> standing. Deterministic from the seed.
    /// </summary>
    /// <param name="sites">The map's forage spots.</param>
    /// <param name="field">The nodes standing now.</param>
    /// <param name="grid">The map's own walk grid.</param>
    /// <param name="reserved">Tiles that must stay clear.</param>
    /// <param name="occupied">Tiles a player stands on.</param>
    /// <param name="seed">Seed for this instance and this day.</param>
    /// <param name="season">The season of the day that begins.</param>
    /// <param name="standing">Forage nodes already standing that stay (those of another season are about to be removed).</param>
    /// <returns>The placements, kind <see cref="NodeKind.Forage"/>, the variant naming the item.</returns>
    public static List<NodePlacement> Forage(IReadOnlyList<NodeSite> sites, NodeField field, WalkGrid grid, IReadOnlySet<(int X, int Y)> reserved, IReadOnlySet<(int X, int Y)> occupied, ulong seed, Season season, int standing)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(occupied);

        List<NodePlacement> placements = [];
        if (sites.Count == 0)
        {
            return placements;
        }

        NodeRandom random = new(seed);
        HashSet<(int, int)> taken = [];
        int attempts = NodeLimits.ForagePerDay * 8;
        while (attempts-- > 0
            && placements.Count < NodeLimits.ForagePerDay
            && standing + placements.Count < NodeLimits.MaxForageStanding
            && field.Count + placements.Count < NodeLimits.MaxPerInstance)
        {
            NodeSite site = sites[random.Next(sites.Count)];
            if (field.HasNodeAt(site.X, site.Y)
                || grid.IsBlocked(site.X, site.Y)
                || occupied.Contains((site.X, site.Y))
                || NearReserved(reserved, site.X, site.Y)
                || !taken.Add((site.X, site.Y)))
            {
                continue;
            }

            if (ForageCatalog.Roll(season, ref random) is not { } variant)
            {
                break;
            }

            placements.Add(new NodePlacement(site.X, site.Y, NodeKind.Forage, variant, 0));
        }

        return placements;
    }

    private static int ChanceOf(NodeKind kind) => kind switch
    {
        NodeKind.Tree => TreeChance,
        NodeKind.Weed => WeedChance,
        NodeKind.Stone => StoneChance,
        NodeKind.Twig => TwigChance,
        NodeKind.Grass => GrassChance,
        _ => 0,
    };

    // A reserved tile and its eight neighbours stay free of nodes, so an arrival point can never be walled in.
    private static bool NearReserved(IReadOnlySet<(int X, int Y)> reserved, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (reserved.Contains((x + dx, y + dy)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
