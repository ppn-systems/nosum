// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Chunks;

namespace Nosum.Wasm.Socket.Farm;

/// <summary>One worked tile as the client mirrors it. Days are game day numbers (docs/adr/ADR-021 D7).</summary>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="CropId">Planted crop id; zero for bare soil.</param>
/// <param name="PlantedAtMs">When the crop was planted, Unix milliseconds on the server's clock.</param>
/// <param name="GrowthDays">Watered game days settled so far, not counting <paramref name="WateredDay"/>.</param>
/// <param name="WateredDay">The last game day the soil was watered; zero when never.</param>
/// <param name="TendedDay">The game day the bare soil was tilled or its crop harvested; zero with a crop.</param>
/// <param name="Withered">Whether the server says the crop has withered (sprinklers and rain counted by the server, never guessed here).</param>
public readonly record struct FarmTileInfo(int X, int Y, int CropId, long PlantedAtMs, int GrowthDays, int WateredDay, int TendedDay, bool Withered);

/// <summary>
/// The client's mirror of the worked tiles of the player's map, plus an estimate of the server's clock. The server sends a chunk's tiles once, when the chunk joins the player's
/// interest set (<see cref="ChunkState"/>), and then only changes in it; when the chunk leaves the set (<see cref="ChunkLeft"/>) its tiles are dropped, so the mirror holds
/// the chunks around the player and nothing else (docs/adr/ADR-022 D6). A tile belongs to the chunk its coordinates fall in.
/// The server sends the stored growth numbers, not stages: the frame loop computes each crop's stage with the
/// shared rules, so crops grow on screen without a push per stage. The frame loop reads the mirror with
/// <see cref="Read"/>, <see cref="Count"/> and <see cref="TileAt"/> and never allocates or awaits.
/// </summary>
public sealed class FarmChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly ChunkLoadGate _loads = new();
    private readonly List<FarmTileInfo> _tiles = [];
    private readonly Dictionary<int, int> _index = [];
    private string _mapId = string.Empty;
    private long _clockOffsetMs;
    private bool _loaded;

    /// <summary>Initializes the channel and starts listening for pushed tile changes.</summary>
    /// <param name="socket">The game connection.</param>
    public FarmChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _subscriptions = [socket.Subscribe<FarmTilesChanged>(this.OnChanged), socket.Subscribe<FarmTilesCleared>(this.OnCleared), socket.Subscribe<ChunkState>(this.OnChunkState), socket.Subscribe<ChunkLeft>(this.OnChunkLeft)];
    }

    /// <summary>Raised when the server answered a use of an item. May run on any thread.</summary>
    public event Action<FarmStatus, FarmAction, int>? UseCompleted;

    /// <summary>Raised when tiles were loaded or changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the key of the map the mirrored tiles belong to; empty before the first load.</summary>
    public string MapId
    {
        get
        {
            lock (_gate)
            {
                return _mapId;
            }
        }
    }

    /// <summary>Gets the number of worked tiles. Use under <see cref="Read"/> when iterating.</summary>
    public int Count => _tiles.Count;

    /// <summary>Takes the lock that keeps the mirror steady while the frame loop iterates it; dispose to release.</summary>
    /// <returns>The held lock.</returns>
    public Lock.Scope Read() => _gate.EnterScope();

    /// <summary>Reads a tile by position in the list; call under <see cref="Read"/>.</summary>
    /// <param name="index">Zero-based position, below <see cref="Count"/>.</param>
    /// <returns>The tile.</returns>
    public FarmTileInfo TileAt(int index) => _tiles[index];

    /// <summary>Looks up the tile at a position; call under <see cref="Read"/>.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <param name="tile">The tile when it is worked.</param>
    /// <returns><see langword="true"/> when the tile is tilled.</returns>
    public bool TryGetTile(int x, int y, out FarmTileInfo tile)
    {
        if (_index.TryGetValue(Key(x, y), out int at))
        {
            tile = _tiles[at];
            return true;
        }

        tile = default;
        return false;
    }

    /// <summary>Estimates the server's wall clock now, Unix milliseconds (the local clock plus the offset the last server message revealed).</summary>
    /// <returns>The estimate; the local clock until the first server message.</returns>
    public long ServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Volatile.Read(ref _clockOffsetMs);

    /// <summary>Reads the worked tiles of the chunks the player can see and replaces the mirror with them. Call after joining a map: it also names the map the mirror belongs to. Later chunks arrive as pushes.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Asks the server to use the item in a hotbar slot on a tile; the answer arrives as <see cref="UseCompleted"/> and the change as a pushed tile.</summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    /// <param name="tileX">Target tile x.</param>
    /// <param name="tileY">Target tile y.</param>
    public void Use(int slot, int tileX, int tileY) => _ = this.UseAsync(slot, tileX, tileY);

    /// <summary>
    /// Asks the server to use the item in a hotbar slot on a line of tiles (docs/adr/ADR-026 D1): the wide use of an upgraded hoe or watering can, from the tile
    /// in front of the farmer on, the way they face. The server checks the length against the item in the slot.
    /// </summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    /// <param name="tileX">First tile x.</param>
    /// <param name="tileY">First tile y.</param>
    /// <param name="tiles">How many tiles in a line: 1, 3 or 5.</param>
    public void UseArea(int slot, int tileX, int tileY, int tiles) => _ = this.UseAreaAsync(slot, tileX, tileY, tiles);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private static int Key(int x, int y) => (y << 16) | (x & 0xFFFF);

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the next join completes; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        int gen = _loads.BeginLoad();
        try
        {
            using PacketScope<GetFarm> request = PacketFactory<GetFarm>.Acquire();
            FarmSnapshot snapshot = await _socket.RequestAsync<FarmSnapshot>(request.Value).ConfigureAwait(false);
            if (!_loads.IsCurrent(gen))
            {
                return;
            }

            lock (_gate)
            {
                _loaded = false;
                _tiles.Clear();
                _index.Clear();
                _mapId = snapshot.MapId;
                this.NoteServerClock(snapshot.ServerNowMs);
                foreach (FarmTileRow row in snapshot.Tiles)
                {
                    this.Store(row);
                }

                _loaded = true;
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
        finally
        {
            _loads.EndLoad();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost action changes nothing; the player tries again.")]
    private async Task UseAsync(int slot, int tileX, int tileY)
    {
        try
        {
            using PacketScope<UseItem> request = PacketFactory<UseItem>.Acquire();
            request.Value.Slot = (byte)slot;
            request.Value.TileX = tileX;
            request.Value.TileY = tileY;
            UseItemResult result = await _socket.RequestAsync<UseItemResult>(request.Value).ConfigureAwait(false);
            this.UseCompleted?.Invoke(result.Status, result.Action, result.Worked);
        }
        catch (Exception)
        {
            // The request was lost (connection dropped, or no reply): nothing is known to have been applied, so the player is told to try again
            // instead of seeing a press that silently did nothing.
            this.UseCompleted?.Invoke(FarmStatus.Conflict, FarmAction.None, 0);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost action changes nothing; the player tries again.")]
    private async Task UseAreaAsync(int slot, int tileX, int tileY, int tiles)
    {
        try
        {
            using PacketScope<UseItemArea> request = PacketFactory<UseItemArea>.Acquire();
            request.Value.Slot = (byte)slot;
            request.Value.TileX = tileX;
            request.Value.TileY = tileY;
            request.Value.Tiles = (byte)tiles;
            UseItemResult result = await _socket.RequestAsync<UseItemResult>(request.Value).ConfigureAwait(false);
            this.UseCompleted?.Invoke(result.Status, result.Action, result.Worked);
        }
        catch (Exception)
        {
            // The request was lost (connection dropped, or no reply): nothing is known to have been applied, so the player is told to try again
            // instead of seeing a press that silently did nothing.
            this.UseCompleted?.Invoke(FarmStatus.Conflict, FarmAction.None, 0);
        }
    }

    private void OnChunkState(ChunkState packet)
    {
        lock (_gate)
        {
            if (!_loads.TryAcceptChunkMap(ref _mapId, packet.MapId, () =>
            {
                _tiles.Clear();
                _index.Clear();
            }))
            {
                return;
            }

            _ = this.RemoveChunk(new ChunkCoord(packet.ChunkX, packet.ChunkY));
            if (packet.FarmTiles.Count > 0)
            {
                this.NoteServerClock(packet.ServerNowMs);
            }

            foreach (FarmTileRow row in packet.FarmTiles)
            {
                this.Store(row);
            }
        }

        this.Changed?.Invoke();
    }

    private void OnChunkLeft(ChunkLeft packet)
    {
        bool removed;
        lock (_gate)
        {
            removed = string.Equals(packet.MapId, _mapId, StringComparison.Ordinal) && this.RemoveChunk(new ChunkCoord(packet.ChunkX, packet.ChunkY));
        }

        if (removed)
        {
            this.Changed?.Invoke();
        }
    }

    // Drops the tiles of one chunk. Call under the lock.
    private bool RemoveChunk(ChunkCoord chunk)
    {
        int kept = 0;
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (ChunkCoord.OfTile(_tiles[i].X, _tiles[i].Y) != chunk)
            {
                _tiles[kept++] = _tiles[i];
            }
        }

        if (kept == _tiles.Count)
        {
            return false;
        }

        _tiles.RemoveRange(kept, _tiles.Count - kept);
        _index.Clear();
        for (int i = 0; i < _tiles.Count; i++)
        {
            _index[Key(_tiles[i].X, _tiles[i].Y)] = i;
        }

        return true;
    }

    private void OnChanged(FarmTilesChanged packet)
    {
        lock (_gate)
        {
            // A push for another map (a late one after a map change) is not ours.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            this.NoteServerClock(packet.ServerNowMs);
            foreach (FarmTileRow row in packet.Tiles)
            {
                this.Store(row);
            }
        }

        this.Changed?.Invoke();
    }

    // A pickaxe cleared tilled soil (docs/adr/ADR-021): the tiles are plain ground again.
    private void OnCleared(FarmTilesCleared packet)
    {
        lock (_gate)
        {
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            foreach (TileRow row in packet.Tiles)
            {
                this.Remove(Key(row.X, row.Y));
            }
        }

        this.Changed?.Invoke();
    }

    // Call under the lock. Swaps the last tile into the hole so the list stays dense.
    private void Remove(int key)
    {
        if (!_index.Remove(key, out int at))
        {
            return;
        }

        int last = _tiles.Count - 1;
        if (at != last)
        {
            FarmTileInfo moved = _tiles[last];
            _tiles[at] = moved;
            _index[Key(moved.X, moved.Y)] = at;
        }

        _tiles.RemoveAt(last);
    }

    /// <summary>Gets whether the worked tiles of the player's map have arrived. Read it from any thread.</summary>
    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    /// <summary>
    /// Counts the planted crops on the mirrored map that one more dry game day would wither (docs/adr/ADR-021 D7). A crop the server has already
    /// withered is not counted. Outdoor maps count rain as water, as the server does. Changes nothing; the thirsty warning reads it.
    /// </summary>
    /// <param name="rules">The server calendar, rebuilt by the caller.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="sprinklerSinceMs">When the first sprinkler beside a tile was placed, or null (<c>PlaceableChannel.SprinklerWateredSinceMs</c>).</param>
    /// <returns>The number of crops at risk.</returns>
    public int CountAtRisk(GameCalendar rules, long nowMs, Func<int, int, long?> sprinklerSinceMs)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(sprinklerSinceMs);

        int count = 0;
        lock (_gate)
        {
            bool outdoors = !(GameMaps.TryGet(_mapId, out MapSpec? spec) && spec.Indoors);
            foreach (FarmTileInfo info in _tiles)
            {
                if (info.CropId != 0 && !info.Withered && FarmRules.IsAtRisk(TileOf(info), nowMs, rules, outdoors, sprinklerSinceMs(info.X, info.Y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Returns the stored numbers of a mirrored tile as the shared rules take them.</summary>
    /// <param name="info">The mirrored tile.</param>
    /// <returns>The tile.</returns>
    public static FarmTile TileOf(in FarmTileInfo info) => new(info.CropId, info.PlantedAtMs, info.GrowthDays, info.WateredDay, info.TendedDay);

    // Call under the lock.
    private void Store(FarmTileRow row)
    {
        int key = Key(row.X, row.Y);
        if (row.Removed)
        {
            this.Remove(key);
            return;
        }

        FarmTileInfo tile = new(row.X, row.Y, row.CropId, row.PlantedAtMs, row.GrowthDays, row.WateredDay, row.TendedDay, row.Withered);
        if (_index.TryGetValue(key, out int at))
        {
            _tiles[at] = tile;
        }
        else
        {
            _index[key] = _tiles.Count;
            _tiles.Add(tile);
        }
    }

    // The offset is the server clock minus ours at the moment a server message arrived (latency counts as clock error, a few tens of milliseconds).
    private void NoteServerClock(long serverNowMs)
    {
        Volatile.Write(ref _clockOffsetMs, serverNowMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
