// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Node;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Core;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Buildings;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // Breaking a resource node with a tool: the other thing a farmer does all day besides the crops.
    private async Task NodesAsync()
    {
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, 1032, 264, 0);
        NodeSnapshot nodes;
        using (GetNodes request = new())
        {
            nodes = await p.RequestAsync<NodeSnapshot>(request);
        }

        int tool = ItemCatalog.Scythe;
        NodeRow? target = nodes.Nodes.FirstOrDefault(n => n.Kind is (byte)NodeKind.Weed or (byte)NodeKind.Grass);
        if (target is null)
        {
            target = nodes.Nodes.FirstOrDefault(n => n.Kind is (byte)NodeKind.Twig or (byte)NodeKind.Stump);
            tool = ItemCatalog.Axe;
        }

        if (target is null)
        {
            this.Failures.Add("nodes: no weed, grass, twig or stump in the interest set of the farm spawn");
            return;
        }

        int slot = await this.SlotOfAsync(p, tool);
        BudgetRig.Stand(p.AccountId, (target.X * 16) + 8, ((target.Y + 1) * 16) + 8);
        _ = await this.UseAsync(p, "break a one-hit node", slot, target.X, target.Y);
    }

    // Walking: steps inside a chunk, steps that move the player into new chunks, and an exit onto another map.
    private async Task WalkAsync()
    {
        IMapCatalog maps = BudgetRig.Service<IMapCatalog>();
        _ = maps.TryGet(GameMaps.Town, out MapDefinition? town);
        CrossingPlan plan = ChunkPlaces.FindCrossing(town!);
        await using BudgetPlayer watcher = await this.FreshAsync(GameMaps.Town, plan.AX, plan.AY, 0);
        await using BudgetPlayer walker = await this.FreshAsync(GameMaps.Town, plan.BX, plan.BY, 0);

        // East along the row, across the border of the chunk, in steps a little under the walking speed.
        double x = plan.BX;
        int crossed = 0;
        ChunkCoord chunk = ChunkCoord.OfPixels(x, plan.BY, town!.Grid.TileSize);
        double stop = (plan.C0 + 2) * ChunkCoord.SizeTiles * plan.Tile;
        while (x < stop + (3 * plan.Tile) && crossed < 3)
        {
            x += 16;
            walker.SetFeet((float)x, (float)plan.BY);
            ChunkCoord now = ChunkCoord.OfPixels(x, plan.BY, town.Grid.TileSize);
            bool joins = now != chunk;
            chunk = now;
            string variant = joins ? "step into a new chunk (interest set changes)" : "step inside a chunk, another player in sight";
            await _rig.MeasureAsync("Player", PlayerOpCode.MoveIntent, "MoveIntent", variant, async () =>
            {
                using MoveIntent intent = new() { Sequence = walker.NextSequence(), X = (float)x, Y = (float)plan.BY, Facing = 1, Moving = true };
                await walker.SendAsync(intent);
                return true;
            });
            if (joins)
            {
                crossed++;
            }
        }
    }

    // Walking onto an exit leaves the map and enters another: the cost of a map change.
    private async Task WarpAsync()
    {
        IMapCatalog maps = BudgetRig.Service<IMapCatalog>();
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? map);
        int tile = map!.Grid.TileSize;
        foreach (MapWarp exit in map.Warps.Where(w => w.TargetMap != GameMaps.FarmHouse))
        {
            if (!WarpRule.TryEdge(exit, map.Grid, out int ex, out int ey, out int outward) || outward < 0)
            {
                continue;
            }

            (int dx, int dy) = outward switch { 0 => (0, 1), 1 => (1, 0), 2 => (0, -1), _ => (-1, 0) };
            double fx = (ex - dx + 0.5) * tile;
            double fy = (ey - dy + 0.5) * tile;
            if (MoveValidator.FootBlocked(map.Grid, fx, fy) || MoveValidator.FootBlocked(map.Grid, (ex + 0.5) * tile, (ey + 0.5) * tile))
            {
                continue;
            }

            await using BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, fx, fy, 0);
            BudgetRig.Stand(p.AccountId, fx, fy);
            await Task.Delay(300, _rig.Timeout);
            double tx = (ex + 0.5) * tile;
            double ty = (ey + 0.5) * tile;
            await _rig.MeasureAsync("Player", PlayerOpCode.MoveIntent, "MoveIntent", "step onto an exit: change of map", async () =>
            {
                using MoveIntent intent = new() { Sequence = p.NextSequence(), X = (float)tx, Y = (float)ty, Facing = (byte)outward, Moving = true };
                await p.SendAsync(intent);
                return true;
            });
            Nalix.Abstractions.Networking.IConnection connection = BudgetRig.Service<Nosum.Abstractions.Accounts.ISessionRegistry>().GetActive(p.AccountId)!;
            _ = BudgetRig.Service<Nosum.Abstractions.Presence.IMapPresence>().TryGet(connection, out Nosum.Abstractions.Presence.PlayerPresence? after);
            if (after?.MapId == GameMaps.Farm)
            {
                this.Failures.Add("warp: the step onto the exit (" + exit.FromX + "," + exit.FromY + " -> " + exit.TargetMap + ") did not change the map");
            }

            return;
        }

        this.Failures.Add("warp: no usable edge exit with a free tile beside it");
    }

    // The jobs the host runs on a timer, one pass each.
    private async Task BackgroundAsync()
    {
        _ = await _rig.MeasureAsync("Background", null, "GuestCleanup", "hourly pass, nothing old", async () => await new GuestCleanupService(_rig.Options, _rig.Clock).RunOnceAsync(_rig.Timeout));
        _ = await _rig.MeasureAsync("Background", null, "MarketExpiry", "every minute, nothing due", async () => await BudgetRig.Service<IMarketService>().ExpireDueAsync(_rig.Timeout));
        _ = await _rig.MeasureAsync("Background", null, "MailExpiry", "hourly pass, nothing due", async () => await BudgetRig.Service<IMailService>().PurgeExpiredAsync(_rig.Timeout));
        _ = await _rig.MeasureAsync("Background", null, "GroundItemExpiry", "every 30 s, nothing due", async () => await BudgetRig.Service<IGroundItems>().ExpireDueAsync(_rig.Timeout));
        _ = await _rig.MeasureAsync("Background", null, "AnimalWander", "tick, nobody inside any building", async () =>
        {
            await BudgetRig.Service<AnimalWanderService>().TickAsync(_rig.Clock.GetUtcNow().ToUnixTimeMilliseconds(), _rig.Timeout);
            return true;
        });
    }
}
