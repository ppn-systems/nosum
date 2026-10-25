// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Mines;

/// <summary>
/// Decides what stands on a mine floor each game day (docs/adr/ADR-026 D3): stones everywhere, copper ore on floors 1 to 39, iron ore on floors
/// 20 to 40 and, on every floor but the last, one ladder hidden under a stone. Deterministic from the seed
/// (<see cref="MineRules.FloorSeed"/>), so a test pins every floor and the server never stores a floor.
/// </summary>
public static class MineFloorPlanner
{
    // Nothing stands this close to the arrival tile, so a player always has room to move and never starts walled in.
    private const int ClearRadius = 2;

    /// <summary>What a floor holds.</summary>
    /// <param name="Nodes">Every node, in a stable order.</param>
    /// <param name="Ladder">The tile of the stone that hides the ladder; <see langword="null"/> on the last floor.</param>
    public sealed record Plan(IReadOnlyList<NodePlacement> Nodes, (int X, int Y)? Ladder);

    /// <summary>Plans a floor.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MineRules.MaxFloor"/>.</param>
    /// <param name="seed">The floor's seed (<see cref="MineRules.FloorSeed"/>).</param>
    /// <param name="grid">The floor plan's walk grid.</param>
    /// <param name="reserved">Tiles that must stay clear (the arrival tile and its north neighbour).</param>
    /// <param name="spawnX">Arrival tile x.</param>
    /// <param name="spawnY">Arrival tile y.</param>
    /// <returns>The plan. Only tiles a player can reach on foot from the arrival tile get a node.</returns>
    public static Plan Build(int floor, ulong seed, WalkGrid grid, IReadOnlySet<(int X, int Y)> reserved, int spawnX, int spawnY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(floor, MineRules.MaxFloor);

        int[] distance = Distances(grid, spawnX, spawnY);
        List<(int X, int Y)> candidates = [];
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (distance[(y * grid.Width) + x] > 0
                    && Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY)) > ClearRadius
                    && !NearReserved(reserved, x, y))
                {
                    candidates.Add((x, y));
                }
            }
        }

        // A deterministic shuffle: the same seed puts the same nodes on the same tiles on every machine.
        NodeRandom random = new(seed);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int reachable = candidates.Count;
        int stones = Math.Clamp(reachable / 10, 6, 120);
        int copper = MineRules.HasCopper(floor) ? 3 + (reachable / 120) : 0;
        int iron = MineRules.HasIron(floor) ? 2 + (reachable / 120) + ((floor - MineRules.IronMinFloor) / 5) : 0;

        List<NodePlacement> nodes = [];
        int next = 0;
        for (int i = 0; i < stones && next < candidates.Count; i++, next++)
        {
            nodes.Add(new NodePlacement(candidates[next].X, candidates[next].Y, NodeKind.Stone, 0, 0));
        }

        for (int i = 0; i < copper && next < candidates.Count; i++, next++)
        {
            nodes.Add(new NodePlacement(candidates[next].X, candidates[next].Y, NodeKind.CopperOre, 0, 0));
        }

        for (int i = 0; i < iron && next < candidates.Count; i++, next++)
        {
            nodes.Add(new NodePlacement(candidates[next].X, candidates[next].Y, NodeKind.IronOre, 0, 0));
        }

        (int X, int Y)? ladder = null;
        int stoneCount = Math.Min(stones, nodes.Count);
        if (MineRules.HasLadder(floor) && stoneCount > 0)
        {
            // The ladder hides under one of the stones farthest from the arrival tile, so every floor sends the player across it.
            List<int> byDistance = [];
            for (int i = 0; i < stoneCount; i++)
            {
                byDistance.Add(i);
            }

            byDistance.Sort((a, b) =>
            {
                int byFar = distance[(nodes[b].Y * grid.Width) + nodes[b].X].CompareTo(distance[(nodes[a].Y * grid.Width) + nodes[a].X]);
                return byFar != 0 ? byFar : a.CompareTo(b);
            });
            int pick = byDistance[random.Next(Math.Max(1, byDistance.Count / 4))];
            NodePlacement hidden = nodes[pick];
            nodes[pick] = hidden with { Variant = NodeVariants.LadderStone };
            ladder = (hidden.X, hidden.Y);
        }

        return new Plan(nodes, ladder);
    }

    // Steps from the arrival tile to every tile a player can walk to (four neighbours); -1 for the rest, zero for the arrival tile itself.
    private static int[] Distances(WalkGrid grid, int startX, int startY)
    {
        int[] distance = new int[grid.Width * grid.Height];
        Array.Fill(distance, -1);
        if (grid.IsBlocked(startX, startY))
        {
            return distance;
        }

        Queue<(int X, int Y)> queue = new();
        distance[(startY * grid.Width) + startX] = 0;
        queue.Enqueue((startX, startY));
        ReadOnlySpan<(int Dx, int Dy)> steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int here = distance[(y * grid.Width) + x];
            foreach ((int dx, int dy) in steps)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height || grid.IsBlocked(nx, ny) || distance[(ny * grid.Width) + nx] >= 0)
                {
                    continue;
                }

                distance[(ny * grid.Width) + nx] = here + 1;
                queue.Enqueue((nx, ny));
            }
        }

        return distance;
    }

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
