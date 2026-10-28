// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Weather;

namespace Nosum.Infrastructure.Farm;

public sealed partial class FarmService
{
    /// <inheritdoc/>
    public async Task<FarmSnapshotView> GetFarmAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return new FarmSnapshotView(string.Empty, nowMs, []);
        }

        MapFarm farm = await this.FarmOfAsync(player.Instance, cancellationToken).ConfigureAwait(false);
        using (await _mapLocks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            // Only what the player can see: the tiles of the chunks in their interest set (all of them before the player has one).
            List<FarmTileView> tiles = new(farm.Count);
            foreach (KeyValuePair<ChunkCoord, Dictionary<(int X, int Y), FarmTile>> chunk in farm.Chunks)
            {
                if (player.Viewer is not null && !_chunks.IsInterested(player, chunk.Key))
                {
                    continue;
                }

                foreach (KeyValuePair<(int X, int Y), FarmTile> tile in chunk.Value)
                {
                    if (this.LiveTile(farm, player.Instance, tile.Key.X, tile.Key.Y, nowMs) is null)
                    {
                        continue;
                    }

                    tiles.Add(new FarmTileView(tile.Key.X, tile.Key.Y, tile.Value));
                }
            }

            return new FarmSnapshotView(map.Id, nowMs, tiles);
        }
    }

    /// <inheritdoc/>
    public async ValueTask FillAsync(MapInstance instance, ChunkCoord chunk, ChunkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return;
        }

        MapFarm farm = await this.FarmOfAsync(instance, cancellationToken).ConfigureAwait(false);
        using (await _mapLocks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            if (farm.Chunks.TryGetValue(chunk, out Dictionary<(int X, int Y), FarmTile>? tiles))
            {
                long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
                state.ServerNowMs = nowMs;
                foreach (KeyValuePair<(int X, int Y), FarmTile> tile in tiles)
                {
                    if (this.LiveTile(farm, instance, tile.Key.X, tile.Key.Y, nowMs) is null)
                    {
                        continue;
                    }

                    state.FarmTiles.Add(ToRow(tile.Key.X, tile.Key.Y, tile.Value));
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Forget(MapInstance instance) => _ = _farms.TryRemove(instance.Key, out _);

    /// <inheritdoc/>
    public async Task<IReadOnlySet<(int X, int Y)>> WorkedTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default)
    {
        HashSet<(int X, int Y)> found = [];
        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return found;
        }

        MapFarm farm = await this.FarmOfAsync(instance, cancellationToken).ConfigureAwait(false);
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        using (await _mapLocks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            for (int y = tileY; y < tileY + height; y++)
            {
                for (int x = tileX; x < tileX + width; x++)
                {
                    // Bare soil that has reverted is no longer worked, so a piece may stand there.
                    if (this.LiveTile(farm, instance, x, y, nowMs) is not null)
                    {
                        _ = found.Add((x, y));
                    }
                }
            }
        }

        return found;
    }

    private static FarmTileRow ToRow(int x, int y, FarmTile tile)
    {
        FarmTileRow row = FarmTileRow.Create();
        row.X = (ushort)x;
        row.Y = (ushort)y;
        row.CropId = tile.CropId;
        row.PlantedAtMs = tile.PlantedAtMs;
        row.GrowthDays = tile.GrowthDays;
        row.WateredDay = tile.WateredDay;
        return row;
    }

    private static FarmTileRow ToRemovedRow(int x, int y)
    {
        FarmTileRow row = FarmTileRow.Create();
        row.X = (ushort)x;
        row.Y = (ushort)y;
        row.Removed = true;
        return row;
    }

    // Loads a map's tilled tiles once; a failed load is forgotten so the next call tries again.
    private async Task<MapFarm> FarmOfAsync(MapInstance instance, CancellationToken cancellationToken)
    {
        string key = instance.Key;
        Task<MapFarm> load = _farms.GetOrAdd(key, _ => this.LoadFarmAsync(instance));
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _ = _farms.TryRemove(new KeyValuePair<string, Task<MapFarm>>(key, load));
            throw;
        }
    }

    private async Task<MapFarm> LoadFarmAsync(MapInstance instance)
    {
        MapFarm farm = new(instance);
        int today = _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<FarmPlot> rows = await unit.Repository<FarmPlot>().ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            foreach (FarmPlot row in rows)
            {
                FarmTile tile = new(row.CropId, row.PlantedAtMs, row.GrowthDays, row.WateredDay, row.TendedDay);

                // Bare soil from before day counting has no history: it starts its dry days now instead of reverting on the spot.
                if (!tile.HasCrop && tile.WateredDay == 0 && tile.TendedDay == 0)
                {
                    tile = tile with { TendedDay = today };
                }

                farm.Set(row.X, row.Y, tile);
            }
        }

        return farm;
    }

    // A tile as the player sees it: null when it was never tilled or its bare soil has reverted. A reverted tile may still sit in memory and in
    // PostgreSQL until the next day sweep; whoever tills it again overwrites it.
    private FarmTile? LiveTile(MapFarm farm, MapInstance instance, int x, int y, long nowMs)
        => farm.TryGet(x, y, out FarmTile found) && !FarmRules.IsReverted(found, instance.Key, x, y, nowMs, _calendar, instance.IsOutdoors, _placed?.IsSprinklerWatered(instance, x, y) == true) ? found : null;

    private void OnDayEnded(GameDayEnded ended)
    {
        _ = ended;
        _ = this.SweepAsync();
    }

    // Drops the reverted tiles of every loaded farm. Not loaded farms need nothing: the rule is computed on read.
    [SuppressMessage("Design", "CA1031", Justification = "A failed sweep is repeated at the next day end; the reverted tiles are already invisible through the lazy rule.")]
    private async Task SweepAsync()
    {
        try
        {
            foreach (Task<MapFarm> load in _farms.Values.ToArray())
            {
                if (load.IsCompletedSuccessfully)
                {
                    await this.SweepFarmAsync(await load.ConfigureAwait(false)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            // See the suppression.
        }
    }

    private async Task SweepFarmAsync(MapFarm farm)
    {
        MapInstance instance = farm.Instance;
        List<(int X, int Y)> gone = [];
        Dictionary<(int X, int Y), FarmTile> settled = [];
        using (await _mapLocks.EnterAsync(instance.Key, CancellationToken.None).ConfigureAwait(false))
        {
            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            int endedDay = _calendar.DayNumberAt(nowMs) - 1;
            foreach (KeyValuePair<ChunkCoord, Dictionary<(int X, int Y), FarmTile>> chunk in farm.Chunks)
            {
                foreach (KeyValuePair<(int X, int Y), FarmTile> tile in chunk.Value)
                {
                    bool sprinkled = _placed?.IsSprinklerWatered(instance, tile.Key.X, tile.Key.Y) == true;
                    if (FarmRules.IsReverted(tile.Value, instance.Key, tile.Key.X, tile.Key.Y, nowMs, _calendar, instance.IsOutdoors, sprinkled))
                    {
                        gone.Add(tile.Key);
                        continue;
                    }

                    if (instance.IsOutdoors && sprinkled && endedDay > 0 && tile.Value.WateredDay < endedDay)
                    {
                        if (tile.Value.HasCrop)
                        {
                            int growth = FarmRules.GrowthDaysAt(tile.Value, nowMs, _calendar, outdoors: true, sprinkled: true);
                            if (growth > 0)
                            {
                                settled[tile.Key] = tile.Value with { GrowthDays = growth - 1, WateredDay = endedDay };
                            }
                        }
                        else
                        {
                            settled[tile.Key] = tile.Value with { WateredDay = endedDay };
                        }
                    }
                }
            }

            if (gone.Count == 0 && settled.Count == 0)
            {
                return;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<FarmPlot> plots = unit.Repository<FarmPlot>();
                foreach (FarmPlot row in await plots.ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false))
                {
                    if (gone.Contains((row.X, row.Y)))
                    {
                        plots.Remove(row);
                    }
                    else if (settled.TryGetValue((row.X, row.Y), out FarmTile updated))
                    {
                        row.GrowthDays = updated.GrowthDays;
                        row.WateredDay = updated.WateredDay;
                        plots.Update(row);
                    }
                }

                _ = await unit.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            foreach ((int X, int Y) key in gone)
            {
                _ = farm.Remove(key.X, key.Y);
            }

            foreach (KeyValuePair<(int X, int Y), FarmTile> pair in settled)
            {
                farm.Set(pair.Key.X, pair.Key.Y, pair.Value);
            }
        }

        if (gone.Count == 0)
        {
            return;
        }

        // The removed rows go to the subscribers of each tile's chunk, one batch per chunk (docs/adr/ADR-022 D4).
        const int BatchSize = 64;
        gone.Sort(static (a, b) => ChunkCoord.OfTile(a.X, a.Y).SortKey.CompareTo(ChunkCoord.OfTile(b.X, b.Y).SortKey));
        int from = 0;
        while (from < gone.Count)
        {
            ChunkCoord chunk = ChunkCoord.OfTile(gone[from].X, gone[from].Y);
            int to = from;
            while (to < gone.Count && ChunkCoord.OfTile(gone[to].X, gone[to].Y) == chunk)
            {
                to++;
            }

            for (int start = from; start < to; start += BatchSize)
            {
                using PacketScope<FarmTilesChanged> lease = PacketFactory<FarmTilesChanged>.Acquire();
                FarmTilesChanged packet = lease.Value;
                packet.MapId = instance.MapId;
                packet.ServerNowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
                packet.Tiles.Clear();
                for (int i = start; i < Math.Min(to, start + BatchSize); i++)
                {
                    packet.Tiles.Add(ToRemovedRow(gone[i].X, gone[i].Y));
                }

                await _publisher.PublishAsync(instance, chunk, packet, null, CancellationToken.None).ConfigureAwait(false);
            }

            from = to;
        }
    }
}
