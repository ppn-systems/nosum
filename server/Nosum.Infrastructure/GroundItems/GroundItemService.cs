// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.World;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.GroundItems;

/// <summary>
/// <see cref="IGroundItems"/>: the items lying in every map instance (docs/adr/ADR-021 D5-D6). An instance's items are loaded once, kept in
/// memory in 16 x 16 tile chunk buckets (docs/adr/ADR-022 D5) and written through to PostgreSQL. Picking up checks only the nine chunks
/// around the player, claims the item in memory so two players cannot take it, and moves it into the inventory with one
/// <see cref="ITransferService"/> transfer that deletes the row in the same transaction. A spawn or pickup is published to the subscribers of the item's
/// chunk, and a chunk's items are sent once when it joins a player's interest set (docs/adr/ADR-022 D4).
/// </summary>
public sealed class GroundItemService : IGroundItems, IChunkStateContributor
{
    private const string PickupReason = "ground.pickup";
    private const int MaxPickupsPerMove = 8;
    private const int MaxStacksPerDrop = 8;
    private const long BlockedRetryMs = 2_000;

    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly ITransferService _transfers;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Task<InstanceItems>> _instances = new(StringComparer.Ordinal);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where.</param>
    /// <param name="chunks">Who is interested in which chunk; a snapshot reply holds only the items in the player's interest set.</param>
    /// <param name="publisher">Delivers a spawn or pickup to the players who can see its chunk.</param>
    /// <param name="maps">The hosted maps (size, tile size).</param>
    /// <param name="transfers">Moves a picked-up stack into the inventory atomically.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    public GroundItemService(IMapPresence presence, IChunkMap chunks, IChunkPublisher publisher, IMapCatalog maps, ITransferService transfers, IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _chunks = chunks;
        _publisher = publisher;
        _maps = maps;
        _transfers = transfers;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<int> DropAsync(GroundDrop drop, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PendingGroundDrop? pending = this.Stage(unit, drop);
            if (pending is null)
            {
                return 0;
            }

            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await this.CompleteAsync(pending, cancellationToken).ConfigureAwait(false);
            return pending.Rows.Count;
        }
    }

    /// <inheritdoc/>
    public PendingGroundDrop? Stage(IUnitOfWork unit, GroundDrop drop)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (drop.Quantity < 1 || !ItemCatalog.TryGet(drop.ItemId, out ItemDefinition? item) || !_maps.TryGet(drop.Instance.MapId, out MapDefinition? map))
        {
            return null;
        }

        int stacks = (drop.Quantity + item.MaxStack - 1) / item.MaxStack;
        int tile = map.Grid.TileSize;
        if (stacks > MaxStacksPerDrop || drop.X < 0 || drop.Y < 0 || drop.X >= map.Grid.Width * tile || drop.Y >= map.Grid.Height * tile)
        {
            return null;
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        GroundItemRules.Timing(drop.Instance.IsPrivate, nowMs, out long priorityUntilMs, out long expiresAtMs);
        IRepository<GroundItem> rows = unit.Repository<GroundItem>();
        List<GroundItem> added = new(stacks);
        int remaining = drop.Quantity;
        for (int i = 0; i < stacks; i++)
        {
            int quantity = Math.Min(remaining, item.MaxStack);
            remaining -= quantity;
            GroundItem row = new()
            {
                MapId = drop.Instance.MapId,
                OwnerId = drop.Instance.OwnerId,
                ItemId = drop.ItemId,
                Quantity = quantity,

                // Several stacks fan out a little so they do not lie exactly on top of each other.
                X = Math.Clamp(drop.X + ((i % 3) * 4), 0, (map.Grid.Width * tile) - 1),
                Y = Math.Clamp(drop.Y + ((i / 3) * 4), 0, (map.Grid.Height * tile) - 1),
                CreatedAtMs = nowMs,
                CausedBy = drop.CausedBy,
                PriorityUntilMs = priorityUntilMs,
                ExpiresAtMs = expiresAtMs,
            };
            rows.Add(row);
            added.Add(row);
        }

        return new PendingGroundDrop(drop.Instance, added);
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(PendingGroundDrop pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        InstanceItems state = await this.StateOfAsync(pending.Instance, cancellationToken).ConfigureAwait(false);
        foreach (GroundItem row in pending.Rows)
        {
            // A load that ran after the save already filed the row.
            state.Add(new Entry(row));
            await this.PublishSpawnedAsync(pending.Instance, state, row, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task OnPlayerMovedAsync(PlayerPresence player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!_instances.TryGetValue(player.Key, out Task<InstanceItems>? load))
        {
            // First time anyone moves here: read what lies on the ground; the next move can pick it up.
            _ = this.StateOfAsync(player.Instance, CancellationToken.None).ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
            return Task.CompletedTask;
        }

        if (!TryLoaded(load, out InstanceItems? state) || state.Count == 0)
        {
            return Task.CompletedTask;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        List<Entry>? picks = state.Claim(player, feetX, feetY, nowMs, MaxPickupsPerMove);
        return picks is null ? Task.CompletedTask : this.PickUpAsync(state, player, picks, feetX, feetY);
    }

    /// <inheritdoc/>
    public async Task<GroundItemsView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new GroundItemsView(string.Empty, []);
        }

        InstanceItems state = await this.StateOfAsync(player.Instance, cancellationToken).ConfigureAwait(false);

        // Only what the player can see: the items of the chunks in their interest set (all of them before the player has one).
        return new GroundItemsView(player.MapId, player.Viewer is null ? state.Snapshot() : state.Snapshot(chunk => _chunks.IsInterested(player, chunk)));
    }

    /// <inheritdoc/>
    public async ValueTask FillAsync(MapInstance instance, ChunkCoord chunk, ChunkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        InstanceItems items = await this.StateOfAsync(instance, cancellationToken).ConfigureAwait(false);
        foreach (GroundItemView item in items.InChunk(chunk))
        {
            state.GroundItems.Add(new GroundItemRow { Id = item.Id, ItemId = item.ItemId, Quantity = item.Quantity, X = (ushort)item.X, Y = (ushort)item.Y });
        }
    }

    /// <inheritdoc/>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        int expired = 0;
        foreach (Task<InstanceItems> load in _instances.Values)
        {
            if (!TryLoaded(load, out InstanceItems? state))
            {
                continue;
            }

            List<Entry>? due = state.ClaimExpired(nowMs);
            if (due is null)
            {
                continue;
            }

            expired += await this.ExpireAsync(state, due, cancellationToken).ConfigureAwait(false);
        }

        return expired;
    }

    // The instance's items when their load has finished (never waits).
    private static bool TryLoaded(Task<InstanceItems> load, [NotNullWhen(true)] out InstanceItems? state)
    {
        state = load.IsCompletedSuccessfully ? load.Result : null;
        return state is not null;
    }

    // Deletes the rows of expired entries, then drops them from memory and tells the players. A failed delete gives the claims back.
    [SuppressMessage("Design", "CA1031", Justification = "A failed sweep releases its claims and the next sweep retries; the sweeper must not stop.")]
    private async Task<int> ExpireAsync(InstanceItems state, List<Entry> due, CancellationToken cancellationToken)
    {
        try
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<GroundItem> rows = unit.Repository<GroundItem>();
                foreach (Entry entry in due)
                {
                    long id = entry.Id;
                    GroundItem? row = await rows.FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
                    if (row is not null)
                    {
                        rows.Remove(row);
                    }
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            state.Release(due);
            return 0;
        }

        foreach (Entry entry in due)
        {
            state.Remove(entry);
            await this.PublishTakenAsync(state, entry, GroundItemGone.Expired, 0, 0, cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }

    // Moves each claimed item into the player's inventory; the row goes in the same transaction. Never throws: it runs off the movement path.
    [SuppressMessage("Design", "CA1031", Justification = "Runs after the move was accepted and nobody awaits it; a failure releases the claim so the next move retries.")]
    private async Task PickUpAsync(InstanceItems state, PlayerPresence player, List<Entry> picks, double feetX, double feetY)
    {
        foreach (Entry entry in picks)
        {
            try
            {
                long id = entry.Id;
                TransferStatus status = await _transfers.ExecuteAsync(
                    [new TransferLeg(TransferParty.Player(player.AccountId), entry.ItemId, entry.Quantity)],
                    PickupReason,
                    async (unit, token) =>
                    {
                        IRepository<GroundItem> rows = unit.Repository<GroundItem>();
                        GroundItem? row = await rows.FirstOrDefaultAsync(g => g.Id == id, token).ConfigureAwait(false);
                        if (row is null)
                        {
                            return TransferStatus.Refused;
                        }

                        rows.Remove(row);
                        return TransferStatus.Ok;
                    },
                    CancellationToken.None).ConfigureAwait(false);

                switch (status)
                {
                    case TransferStatus.Ok:
                        state.Remove(entry);
                        await this.PublishTakenAsync(state, entry, GroundItemGone.PickedUp, (ushort)Math.Max(0, feetX), (ushort)Math.Max(0, feetY), CancellationToken.None).ConfigureAwait(false);
                        break;

                    case TransferStatus.Refused:
                        // The row is already gone (another path took it): forget the ghost.
                        state.Remove(entry);
                        await this.PublishTakenAsync(state, entry, GroundItemGone.Expired, 0, 0, CancellationToken.None).ConfigureAwait(false);
                        break;

                    case TransferStatus.NoSpace:
                        // A full inventory leaves the item; do not hammer the database every 100 ms while the player stands there.
                        state.Release(entry, blockedFor: player.AccountId, blockedUntilMs: _time.GetUtcNow().ToUnixTimeMilliseconds() + BlockedRetryMs);
                        break;

                    default:
                        state.Release(entry);
                        break;
                }
            }
            catch (Exception)
            {
                state.Release(entry);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(MapInstance instance, CancellationToken cancellationToken = default)
    {
        _ = _instances.TryRemove(instance.Key, out _);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<GroundItem> table = unit.Repository<GroundItem>();
            List<GroundItem> rows = await table.ListAsync(g => g.MapId == instance.MapId && g.OwnerId == instance.OwnerId, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return;
            }

            foreach (GroundItem row in rows)
            {
                table.Remove(row);
            }

            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Loads an instance's items once; a failed load is forgotten so the next call tries again.
    private async Task<InstanceItems> StateOfAsync(MapInstance instance, CancellationToken cancellationToken)
    {
        string key = instance.Key;
        Task<InstanceItems> load = _instances.GetOrAdd(key, _ => this.LoadAsync(instance));
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _ = _instances.TryRemove(new KeyValuePair<string, Task<InstanceItems>>(key, load));
            throw;
        }
    }

    private async Task<InstanceItems> LoadAsync(MapInstance instance)
    {
        int tileSize = _maps.TryGet(instance.MapId, out MapDefinition? map) ? map.Grid.TileSize : 16;
        InstanceItems state = new(instance, tileSize);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<GroundItem> rows = await unit.Repository<GroundItem>().ListAsync(g => g.MapId == instance.MapId && g.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            foreach (GroundItem row in rows)
            {
                state.Add(new Entry(row));
            }
        }

        return state;
    }

    private async ValueTask PublishSpawnedAsync(MapInstance instance, InstanceItems state, GroundItem row, CancellationToken cancellationToken)
    {
        using PacketScope<GroundItemSpawned> lease = PacketFactory<GroundItemSpawned>.Acquire();
        GroundItemSpawned packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.Item = new GroundItemRow { Id = row.Id, ItemId = row.ItemId, Quantity = row.Quantity, X = (ushort)row.X, Y = (ushort)row.Y };
        await this.PublishAsync(instance, state.ChunkX(row.X), state.ChunkY(row.Y), packet, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishTakenAsync(InstanceItems state, Entry entry, GroundItemGone reason, ushort toX, ushort toY, CancellationToken cancellationToken)
    {
        using PacketScope<GroundItemTaken> lease = PacketFactory<GroundItemTaken>.Acquire();
        GroundItemTaken packet = lease.Value;
        packet.MapId = state.Instance.MapId;
        packet.Id = entry.Id;
        packet.Reason = reason;
        packet.ToX = toX;
        packet.ToY = toY;
        await this.PublishAsync(state.Instance, state.ChunkX(entry.X), state.ChunkY(entry.Y), packet, cancellationToken).ConfigureAwait(false);
    }

    // The one place ground-item events leave the service: the subscribers of the chunk the item lies in (docs/adr/ADR-022 D4).
    private ValueTask PublishAsync(MapInstance instance, int chunkX, int chunkY, IPacket packet, CancellationToken cancellationToken)
        => _publisher.PublishAsync(instance, new ChunkCoord(chunkX, chunkY), packet, null, cancellationToken);

    // One ground item in memory. Claimed means a pickup or the sweeper is working on it: nobody else may touch it.
    private sealed class Entry
    {
        public Entry(GroundItem row)
        {
            this.Id = row.Id;
            this.ItemId = row.ItemId;
            this.Quantity = row.Quantity;
            this.X = row.X;
            this.Y = row.Y;
            this.CausedBy = row.CausedBy;
            this.PriorityUntilMs = row.PriorityUntilMs;
            this.ExpiresAtMs = row.ExpiresAtMs;
        }

        public long Id { get; }

        public int ItemId { get; }

        public int Quantity { get; }

        public int X { get; }

        public int Y { get; }

        public Guid? CausedBy { get; }

        public long PriorityUntilMs { get; }

        public long ExpiresAtMs { get; }

        public bool Claimed { get; set; }

        public Guid BlockedFor { get; set; }

        public long BlockedUntilMs { get; set; }
    }

    // The items of one instance, bucketed by chunk. Every method is a short synchronous critical section under one lock.
    private sealed class InstanceItems
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<long, Entry> _byId = [];
        private readonly Dictionary<(int X, int Y), List<Entry>> _chunks = [];
        private readonly int _tileSize;
        private int _count;

        public InstanceItems(MapInstance instance, int tileSize)
        {
            this.Instance = instance;
            _tileSize = tileSize;
        }

        public MapInstance Instance { get; }

        public int Count => Volatile.Read(ref _count);

        public int ChunkX(int pixelX) => GroundItemRules.ChunkOf(pixelX, _tileSize);

        public int ChunkY(int pixelY) => GroundItemRules.ChunkOf(pixelY, _tileSize);

        public void Add(Entry entry)
        {
            lock (_gate)
            {
                if (!_byId.TryAdd(entry.Id, entry))
                {
                    return;
                }

                (int X, int Y) chunk = (this.ChunkX(entry.X), this.ChunkY(entry.Y));
                if (!_chunks.TryGetValue(chunk, out List<Entry>? bucket))
                {
                    bucket = [];
                    _chunks[chunk] = bucket;
                }

                bucket.Add(entry);
                Volatile.Write(ref _count, _byId.Count);
            }
        }

        public void Remove(Entry entry)
        {
            lock (_gate)
            {
                if (!_byId.Remove(entry.Id))
                {
                    return;
                }

                (int X, int Y) chunk = (this.ChunkX(entry.X), this.ChunkY(entry.Y));
                if (_chunks.TryGetValue(chunk, out List<Entry>? bucket))
                {
                    _ = bucket.Remove(entry);
                    if (bucket.Count == 0)
                    {
                        _ = _chunks.Remove(chunk);
                    }
                }

                Volatile.Write(ref _count, _byId.Count);
            }
        }

        public void Release(Entry entry, Guid blockedFor = default, long blockedUntilMs = 0)
        {
            lock (_gate)
            {
                entry.Claimed = false;
                entry.BlockedFor = blockedFor;
                entry.BlockedUntilMs = blockedUntilMs;
            }
        }

        public void Release(List<Entry> entries)
        {
            lock (_gate)
            {
                foreach (Entry entry in entries)
                {
                    entry.Claimed = false;
                }
            }
        }

        // Claims the items a player may take now: unclaimed, in range, allowed, not expired. Looks only at the nine chunks around the feet.
        public List<Entry>? Claim(PlayerPresence player, double feetX, double feetY, long nowMs, int limit)
        {
            int cx = GroundItemRules.ChunkOf(feetX, _tileSize);
            int cy = GroundItemRules.ChunkOf(feetY, _tileSize);
            List<Entry>? claimed = null;
            lock (_gate)
            {
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    for (int y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!_chunks.TryGetValue((x, y), out List<Entry>? bucket))
                        {
                            continue;
                        }

                        foreach (Entry entry in bucket)
                        {
                            if (entry.Claimed
                                || (claimed is not null && claimed.Count >= limit)
                                || !GroundItemRules.InPickupRange(entry.X, entry.Y, feetX, feetY)
                                || GroundItemRules.IsExpired(entry.ExpiresAtMs, nowMs)
                                || (entry.BlockedFor == player.AccountId && nowMs < entry.BlockedUntilMs)
                                || !GroundItemRules.CanPickUp(this.Instance.IsPrivate, this.Instance.OwnerId, entry.CausedBy, entry.PriorityUntilMs, player.AccountId, nowMs))
                            {
                                continue;
                            }

                            entry.Claimed = true;
                            (claimed ??= []).Add(entry);
                        }
                    }
                }
            }

            return claimed;
        }

        public List<Entry>? ClaimExpired(long nowMs)
        {
            if (this.Count == 0)
            {
                return null;
            }

            List<Entry>? due = null;
            lock (_gate)
            {
                foreach (Entry entry in _byId.Values)
                {
                    if (!entry.Claimed && GroundItemRules.IsExpired(entry.ExpiresAtMs, nowMs))
                    {
                        entry.Claimed = true;
                        (due ??= []).Add(entry);
                    }
                }
            }

            return due;
        }

        public List<GroundItemView> Snapshot(Func<ChunkCoord, bool>? wanted = null)
        {
            lock (_gate)
            {
                List<GroundItemView> items = new(_byId.Count);
                foreach (KeyValuePair<(int X, int Y), List<Entry>> bucket in _chunks)
                {
                    if (wanted is not null && !wanted(new ChunkCoord(bucket.Key.X, bucket.Key.Y)))
                    {
                        continue;
                    }

                    foreach (Entry entry in bucket.Value)
                    {
                        items.Add(new GroundItemView(entry.Id, entry.ItemId, entry.Quantity, entry.X, entry.Y));
                    }
                }

                return items;
            }
        }

        // The items of one chunk: O(the chunk), not O(the map).
        public List<GroundItemView> InChunk(ChunkCoord chunk)
        {
            lock (_gate)
            {
                List<GroundItemView> items = [];
                if (_chunks.TryGetValue((chunk.X, chunk.Y), out List<Entry>? bucket))
                {
                    foreach (Entry entry in bucket)
                    {
                        items.Add(new GroundItemView(entry.Id, entry.ItemId, entry.Quantity, entry.X, entry.Y));
                    }
                }

                return items;
            }
        }
    }
}
