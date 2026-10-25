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
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;

namespace Nosum.Wasm.Socket.Farm;

/// <summary>One worked tile as the client mirrors it. Times are Unix milliseconds on the server's clock.</summary>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="CropId">Planted crop id; zero for bare soil.</param>
/// <param name="PlantedAtMs">When the crop was planted.</param>
/// <param name="GrowthMs">Watered growth time settled up to <paramref name="SettledAtMs"/>.</param>
/// <param name="SettledAtMs">The moment <paramref name="GrowthMs"/> was last settled.</param>
/// <param name="WetUntilMs">The soil is wet until this moment.</param>
public readonly record struct FarmTileInfo(int X, int Y, int CropId, long PlantedAtMs, long GrowthMs, long SettledAtMs, long WetUntilMs);

/// <summary>
/// The client's mirror of the worked tiles of the player's map, plus an estimate of the server's clock. The server sends a chunk's tiles once, when the chunk joins the player's
/// interest set (<see cref="ChunkState"/>), and then only changes in it; when the chunk leaves the set (<see cref="ChunkLeft"/>) its tiles are dropped, so the mirror holds
/// the chunks around the player and nothing else (docs/adr/ADR-022 D6). A tile belongs to the chunk its coordinates fall in.
/// The server sends the stored timestamps, not stages: the frame loop computes each crop's stage with the
/// shared rules, so crops grow on screen without a push per stage. The frame loop reads the mirror with
/// <see cref="Read"/>, <see cref="Count"/> and <see cref="TileAt"/> and never allocates or awaits.
/// </summary>
public sealed class FarmChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<FarmTileInfo> _tiles = [];
    private readonly Dictionary<int, int> _index = [];
    private string _mapId = string.Empty;
    private long _clockOffsetMs;

    /// <summary>Initializes the channel and starts listening for pushed tile changes.</summary>
    /// <param name="socket">The game connection.</param>
    public FarmChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _subscriptions = [socket.Subscribe<FarmTilesChanged>(this.OnChanged), socket.Subscribe<ChunkState>(this.OnChunkState), socket.Subscribe<ChunkLeft>(this.OnChunkLeft)];
    }

    /// <summary>Raised when the server answered a use of an item. May run on any thread.</summary>
    public event Action<FarmStatus, FarmAction>? UseCompleted;

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
        try
        {
            using PacketScope<GetFarm> request = PacketFactory<GetFarm>.Acquire();
            FarmSnapshot snapshot = await _socket.RequestAsync<FarmSnapshot>(request.Value).ConfigureAwait(false);
            lock (_gate)
            {
                _tiles.Clear();
                _index.Clear();
                _mapId = snapshot.MapId;
                this.NoteServerClock(snapshot.ServerNowMs);
                foreach (FarmTileRow row in snapshot.Tiles)
                {
                    this.Store(row);
                }
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
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
            this.UseCompleted?.Invoke(result.Status, result.Action);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was applied.
        }
    }

    private void OnChunkState(ChunkState packet)
    {
        lock (_gate)
        {
            // A chunk of another map (a late push after a map change, or one that beat the load of this map) is not ours; the load covers it.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
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

    // Call under the lock.
    private void Store(FarmTileRow row)
    {
        FarmTileInfo tile = new(row.X, row.Y, row.CropId, row.PlantedAtMs, row.GrowthMs, row.SettledAtMs, row.WetUntilMs);
        int key = Key(row.X, row.Y);
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
