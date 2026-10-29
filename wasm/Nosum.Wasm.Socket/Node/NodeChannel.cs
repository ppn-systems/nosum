// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Chunks;

namespace Nosum.Wasm.Socket.Node;

/// <summary>One resource node as the client mirrors it. <see cref="Kind"/>, <see cref="Variant"/> and <see cref="Stage"/> are the values of <c>NodeKind</c>, <c>TreeVariant</c> and <c>TreeStage</c> in Nosum.Gameplay.</summary>
/// <param name="Id">Node id.</param>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="Kind">What the node is.</param>
/// <param name="Variant">The tree species.</param>
/// <param name="Stage">The tree stage.</param>
/// <param name="Hits">Hits left before it breaks.</param>
/// <param name="HitAtMs">The client clock reading when the node last lost a hit; zero when it never did here. The scene shakes the node for a moment after it.</param>
public readonly record struct NodeInfo(int Id, int X, int Y, byte Kind, byte Variant, byte Stage, byte Hits, long HitAtMs);

/// <summary>
/// The client's mirror of the resource nodes of the player's current place (docs/adr/ADR-021), and of the watering can's water. The server owns
/// both: a swing shows only when its <see cref="NodeChanged"/> push arrives. The frame loop reads the mirror with <see cref="Read"/>,
/// <see cref="Count"/> and <see cref="At"/> and never allocates or awaits; <see cref="Version"/> says when the set changed, so the scene can rebuild its collision grid then.
/// The server sends the nodes of a chunk once, when the chunk joins the player's interest set (<see cref="ChunkState"/>), and then only changes in it; when the chunk leaves the set
/// (<see cref="ChunkLeft"/>) its nodes are dropped (docs/adr/ADR-022 D6). A node belongs to the chunk of its tile.
/// </summary>
public sealed class NodeChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly Func<long> _clock;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<NodeInfo> _nodes = [];
    private readonly Dictionary<int, int> _index = [];
    private string _mapId = string.Empty;
    private int _version;
    private int _waterLeft = 40;
    private int _waterCapacity = 40;
    private readonly ChunkLoadGate _loads = new();

    /// <summary>Initializes the channel and starts listening for pushes.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="clock">Milliseconds on a client clock that only goes forward; defaults to <see cref="Environment.TickCount64"/>. Tests pass their own.</param>
    public NodeChannel(IGameSocket socket, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _clock = clock ?? (() => Environment.TickCount64);
        _subscriptions =
        [
            socket.Subscribe<NodeChanged>(this.OnChanged),
            socket.Subscribe<ToolState>(this.OnToolState),
            socket.Subscribe<ChunkState>(this.OnChunkState),
            socket.Subscribe<ChunkLeft>(this.OnChunkLeft),
        ];
    }

    /// <summary>Raised when the mirror was loaded or changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the water in the watering can changed. May run on any thread.</summary>
    public event Action? ToolChanged;

    /// <summary>Gets the key of the map the mirrored nodes stand on; empty before the first load or on a map without nodes.</summary>
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

    /// <summary>Gets a number that changes whenever the set of nodes (not a mere hit) does.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets the water left in the watering can.</summary>
    public int WaterLeft => Volatile.Read(ref _waterLeft);

    /// <summary>Gets the most water the can holds.</summary>
    public int WaterCapacity => Volatile.Read(ref _waterCapacity);

    /// <summary>Gets the number of mirrored nodes. Use under <see cref="Read"/>.</summary>
    public int Count => _nodes.Count;

    /// <summary>Takes the lock that keeps the mirror steady while the frame loop reads it; dispose to release.</summary>
    /// <returns>The held lock.</returns>
    public Lock.Scope Read() => _gate.EnterScope();

    /// <summary>Reads a node by position in the list; call under <see cref="Read"/>.</summary>
    /// <param name="index">Zero-based position, below <see cref="Count"/>.</param>
    /// <returns>The node.</returns>
    public NodeInfo At(int index) => _nodes[index];

    /// <summary>Loads the nodes of the place the player is in. Call after joining a map.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the next join completes; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        int gen = _loads.BeginLoad();
        try
        {
            using PacketScope<GetNodes> request = PacketFactory<GetNodes>.Acquire();
            NodeSnapshot snapshot = await _socket.RequestAsync<NodeSnapshot>(request.Value).ConfigureAwait(false);
            if (!_loads.IsCurrent(gen))
            {
                return;
            }

            lock (_gate)
            {
                _nodes.Clear();
                _index.Clear();
                _mapId = snapshot.MapId;
                foreach (NodeRow row in snapshot.Nodes)
                {
                    this.Store(row, hit: false);
                }

                Interlocked.Increment(ref _version);
            }

            this.SetWater(snapshot.WaterLeft, snapshot.WaterCapacity);
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

    private void OnChanged(NodeChanged packet)
    {
        lock (_gate)
        {
            // A push for another map (a late one after a map change) is not ours.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            foreach (NodeRow row in packet.Upserted)
            {
                this.Store(row, hit: true);
            }

            foreach (int id in packet.Removed)
            {
                this.Remove(id);
            }

            Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnChunkState(ChunkState packet)
    {
        lock (_gate)
        {
            if (!_loads.TryAcceptChunkMap(ref _mapId, packet.MapId, () =>
            {
                _nodes.Clear();
                _index.Clear();
            }))
            {
                return;
            }

            _ = this.RemoveChunk(new ChunkCoord(packet.ChunkX, packet.ChunkY));
            foreach (NodeRow row in packet.Nodes)
            {
                this.Store(row, hit: false);
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnChunkLeft(ChunkLeft packet)
    {
        bool removed;
        lock (_gate)
        {
            removed = string.Equals(packet.MapId, _mapId, StringComparison.Ordinal) && this.RemoveChunk(new ChunkCoord(packet.ChunkX, packet.ChunkY));
            if (removed)
            {
                _ = Interlocked.Increment(ref _version);
            }
        }

        if (removed)
        {
            this.Changed?.Invoke();
        }
    }

    // Drops the nodes of one chunk and re-indexes the rest. Call under the lock.
    private bool RemoveChunk(ChunkCoord chunk)
    {
        if (_nodes.RemoveAll(n => ChunkCoord.OfTile(n.X, n.Y) == chunk) == 0)
        {
            return false;
        }

        _index.Clear();
        for (int i = 0; i < _nodes.Count; i++)
        {
            _index[_nodes[i].Id] = i;
        }

        return true;
    }

    private void OnToolState(ToolState packet) => this.SetWater(packet.WaterLeft, packet.WaterCapacity);

    private void SetWater(int left, int capacity)
    {
        Volatile.Write(ref _waterCapacity, Math.Max(1, capacity));
        Volatile.Write(ref _waterLeft, left);
        this.ToolChanged?.Invoke();
    }

    // Call under the lock.
    private void Store(NodeRow row, bool hit)
    {
        if (_index.TryGetValue(row.Id, out int at))
        {
            NodeInfo old = _nodes[at];
            _nodes[at] = new NodeInfo(row.Id, row.X, row.Y, row.Kind, row.Variant, row.Stage, row.Hits, hit && row.Hits < old.Hits && row.Kind == old.Kind ? _clock() : old.HitAtMs);
        }
        else
        {
            _index[row.Id] = _nodes.Count;
            _nodes.Add(new NodeInfo(row.Id, row.X, row.Y, row.Kind, row.Variant, row.Stage, row.Hits, 0));
        }
    }

    // Call under the lock. Swaps the last node into the hole so the list stays dense.
    private void Remove(int id)
    {
        if (!_index.Remove(id, out int at))
        {
            return;
        }

        int last = _nodes.Count - 1;
        if (at != last)
        {
            NodeInfo moved = _nodes[last];
            _nodes[at] = moved;
            _index[moved.Id] = at;
        }

        _nodes.RemoveAt(last);
    }
}
