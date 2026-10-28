// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;

namespace Nosum.Wasm.Socket.Building;

/// <summary>
/// The farm buildings and animals the server tells this client about (docs/adr/ADR-028): the buildings of the land the player stands on (so the farm can draw those still under
/// construction), the player's own buildings (the carpenter page), and the animals of the building they are inside, whose walks the server chooses and the client only follows. The
/// frame reads animals through <see cref="Read"/> and <see cref="AnimalAt"/>, which allocate nothing; requests are sent off the frame.
/// </summary>
public sealed partial class BuildingChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly TimeProvider _time;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<BuildingEntry> _here = [];
    private readonly List<BuildingEntry> _mine = [];
    private readonly List<AnimalState> _animals = [];
    private Guid _roomId;
    private byte _roomKind;
    private int _rations;
    private bool _mayTend;
    private int _version;

    /// <summary>Initializes the channel and subscribes to the server's pushes.</summary>
    /// <param name="socket">The connection.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    public BuildingChannel(IGameSocket socket, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _time = time ?? TimeProvider.System;
        _subscriptions =
        [
            socket.Subscribe<BuildingsSnapshot>(this.OnBuildings),
            socket.Subscribe<AnimalsChanged>(this.OnAnimalsChanged),
            socket.Subscribe<AnimalsMoved>(this.OnAnimalsMoved),
        ];
    }

    /// <summary>Raised when a request was answered.</summary>
    public event Action<BuildingResultInfo>? Completed;

    /// <summary>Raised when what the channel knows changed.</summary>
    public event Action? Changed;

    /// <summary>Gets a number that changes whenever the buildings or the room changed.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets the id of the building the player is inside, or <see cref="Guid.Empty"/>.</summary>
    public Guid RoomId
    {
        get
        {
            lock (_gate)
            {
                return _roomId;
            }
        }
    }

    /// <summary>Gets the kind of the building the player is inside.</summary>
    public byte RoomKind
    {
        get
        {
            lock (_gate)
            {
                return _roomKind;
            }
        }
    }

    /// <summary>Gets the rations in the feeding bench of the building the player is inside.</summary>
    public int Rations
    {
        get
        {
            lock (_gate)
            {
                return _rations;
            }
        }
    }

    /// <summary>Gets whether the player may feed, pet and collect in the building they are inside; a visitor only looks.</summary>
    public bool MayTend
    {
        get
        {
            lock (_gate)
            {
                return _mayTend;
            }
        }
    }

    /// <summary>Gets the milliseconds of this channel's clock; the unit of <see cref="AnimalAt"/>.</summary>
    public long NowMs => _time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Takes the channel's lock for a frame's reads.</summary>
    /// <returns>A scope to dispose when done.</returns>
    public Lock.Scope Read() => _gate.EnterScope();

    /// <summary>Gets the player's own buildings, as last listed for the carpenter page.</summary>
    /// <returns>A copy of the list.</returns>
    public IReadOnlyList<BuildingEntry> MyBuildings()
    {
        lock (_gate)
        {
            return [.. _mine];
        }
    }

    /// <summary>Tells whether a building is finished now.</summary>
    /// <param name="building">The building.</param>
    /// <returns><see langword="true"/> from its finishing time on.</returns>
    public bool IsFinished(BuildingEntry building) => building.FinishesAt <= _time.GetUtcNow();

    /// <summary>Tells whether the placed object is a building that is still being built.</summary>
    /// <param name="objectId">The placed object's id.</param>
    /// <returns><see langword="true"/> while under construction. Allocates nothing.</returns>
    public bool IsUnderConstruction(int objectId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (BuildingEntry building in _here)
            {
                if (building.ObjectId == objectId)
                {
                    return now < building.FinishesAt;
                }
            }
        }

        return false;
    }

    /// <summary>Asks for the buildings of the land the player stands on.</summary>
    public void LoadHere() => _ = this.LoadBuildingsAsync(mine: false);

    /// <summary>Asks for the player's own buildings (the carpenter page).</summary>
    public void LoadMine() => _ = this.LoadBuildingsAsync(mine: true);

    /// <summary>Forgets the buildings of the land: the player left it.</summary>
    public void ClearHere()
    {
        lock (_gate)
        {
            if (_here.Count == 0)
            {
                return;
            }

            _here.Clear();
            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    /// <summary>Asks the carpenter for a building's blueprint.</summary>
    /// <param name="kind">The building.</param>
    public void Commission(byte kind) => _ = this.CommissionAsync(kind);

    /// <summary>Asks the carpenter for an animal.</summary>
    /// <param name="buildingId">The building it will live in.</param>
    /// <param name="kind">The species.</param>
    /// <param name="name">The name the player gave it.</param>
    public void BuyAnimal(Guid buildingId, byte kind, string name) => _ = this.BuyAnimalAsync(buildingId, kind, name);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next map change; there is no caller to report to.")]
    private async Task LoadBuildingsAsync(bool mine)
    {
        try
        {
            using PacketScope<GetBuildings> request = PacketFactory<GetBuildings>.Acquire();
            request.Value.Mine = mine;
            BuildingsSnapshot snapshot = await _socket.RequestAsync<BuildingsSnapshot>(request.Value).ConfigureAwait(false);
            this.StoreBuildings(snapshot, mine);
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task CommissionAsync(byte kind)
    {
        try
        {
            using PacketScope<CommissionBuilding> request = PacketFactory<CommissionBuilding>.Acquire();
            request.Value.Kind = kind;
            BuildingResult result = await _socket.RequestAsync<BuildingResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new BuildingResultInfo(BuildingAction.Commission, result.Status, result.ItemId, 0, Guid.Empty));
        }
        catch (Exception)
        {
            // Connection dropped; nothing was bought.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task BuyAnimalAsync(Guid buildingId, byte kind, string name)
    {
        try
        {
            using PacketScope<BuyAnimal> request = PacketFactory<BuyAnimal>.Acquire();
            request.Value.BuildingId = buildingId;
            request.Value.Kind = kind;
            request.Value.Name = name;
            BuildingResult result = await _socket.RequestAsync<BuildingResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new BuildingResultInfo(BuildingAction.BuyAnimal, result.Status, 0, 0, result.Id));
            if (result.Status == BuildingStatus.Ok)
            {
                // The list on the carpenter page now has one more animal.
                this.LoadMine();
            }
        }
        catch (Exception)
        {
            // Connection dropped; nothing was bought.
        }
    }

    // A reply to a request, or a push to everybody on the land: the same shape.
    private void StoreBuildings(BuildingsSnapshot snapshot, bool mine)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            List<BuildingEntry> target = mine ? _mine : _here;
            target.Clear();
            foreach (BuildingRow row in snapshot.Buildings)
            {
                target.Add(new BuildingEntry(row.Id, row.ObjectId, row.Kind, row.MapId, now.AddMilliseconds(row.RemainingMs), row.Animals, row.Capacity));
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnBuildings(BuildingsSnapshot snapshot) => this.StoreBuildings(snapshot, snapshot.Mine);
}
