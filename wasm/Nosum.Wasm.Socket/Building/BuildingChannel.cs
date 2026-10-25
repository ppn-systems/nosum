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

/// <summary>What a request of the building channel was.</summary>
public enum BuildingAction
{
    /// <summary>Buying a blueprint at the carpenter.</summary>
    Commission = 1,

    /// <summary>Buying an animal at the carpenter.</summary>
    BuyAnimal = 2,

    /// <summary>Putting feed into the feeding bench.</summary>
    Feed = 3,

    /// <summary>Petting an animal.</summary>
    Pet = 4,

    /// <summary>Collecting an animal's produce.</summary>
    Collect = 5,
}

/// <summary>The server's answer to a request.</summary>
/// <param name="Action">What was asked.</param>
/// <param name="Status">How it went.</param>
/// <param name="ItemId">The blueprint, the feed or the produce; zero when none.</param>
/// <param name="Friendship">The animal's friendship after petting; zero otherwise.</param>
/// <param name="AnimalId">The animal asked about, or <see cref="Guid.Empty"/>.</param>
public readonly record struct BuildingResultInfo(BuildingAction Action, BuildingStatus Status, int ItemId, int Friendship, Guid AnimalId);

/// <summary>One farm building as the client knows it: what the carpenter page lists and what the farm draws under construction (docs/adr/ADR-028).</summary>
/// <param name="Id">The building's id.</param>
/// <param name="ObjectId">The placed object standing on the land.</param>
/// <param name="Kind">The kind (<c>BuildingKind</c> in Nosum.Gameplay; this layer holds the wire byte).</param>
/// <param name="MapId">The land's map id.</param>
/// <param name="FinishesAt">When the building is finished, on the clock of this channel.</param>
/// <param name="Animals">Animals living in it.</param>
/// <param name="Capacity">Animals it holds.</param>
public readonly record struct BuildingEntry(Guid Id, int ObjectId, byte Kind, string MapId, DateTimeOffset FinishesAt, int Animals, int Capacity);

/// <summary>An animal of the building the player is in, drawn at a position computed from its walk.</summary>
/// <param name="Id">The animal's id.</param>
/// <param name="Kind">The species (<c>AnimalKind</c> in Nosum.Gameplay; this layer holds the wire byte).</param>
/// <param name="Name">Its name.</param>
/// <param name="Friendship">Friendship, 0 to 1000.</param>
/// <param name="Petted">Whether it was petted today.</param>
/// <param name="HasProduce">Whether it has produce to collect today.</param>
/// <param name="X">Feet x now, interior pixels.</param>
/// <param name="Y">Feet y now, interior pixels.</param>
/// <param name="Facing">0 down, 1 right, 2 up, 3 left: the way the walk goes.</param>
/// <param name="Walking">Whether it is on a walk now.</param>
public readonly record struct AnimalPose(Guid Id, byte Kind, string Name, int Friendship, bool Petted, bool HasProduce, double X, double Y, int Facing, bool Walking);

/// <summary>
/// The farm buildings and animals the server tells this client about (docs/adr/ADR-028): the buildings of the land the player stands on (so the farm can draw those still under
/// construction), the player's own buildings (the carpenter page), and the animals of the building they are inside, whose walks the server chooses and the client only follows. The
/// frame reads animals through <see cref="Read"/> and <see cref="AnimalAt"/>, which allocate nothing; requests are sent off the frame.
/// </summary>
public sealed class BuildingChannel : IDisposable
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

    /// <summary>Gets how many animals the building the player is inside holds. Read under <see cref="Read"/>.</summary>
    public int AnimalCount => _animals.Count;

    /// <summary>Returns an animal with its position at a time. Read under <see cref="Read"/>; allocates nothing.</summary>
    /// <param name="index">Zero to <see cref="AnimalCount"/> minus one.</param>
    /// <param name="nowMs">The time, from <see cref="NowMs"/>.</param>
    /// <returns>The animal.</returns>
    public AnimalPose AnimalAt(int index, long nowMs) => _animals[index].PoseAt(nowMs);

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

    /// <summary>Finds the animal nearest to a point, within a distance. Read under <see cref="Read"/>; allocates nothing.</summary>
    /// <param name="x">Point x, interior pixels.</param>
    /// <param name="y">Point y, interior pixels.</param>
    /// <param name="maxDistance">The farthest an animal may be, pixels.</param>
    /// <param name="nowMs">The time, from <see cref="NowMs"/>.</param>
    /// <param name="animal">The animal when one is near.</param>
    /// <returns><see langword="true"/> when an animal is within the distance.</returns>
    public bool TryFindNear(double x, double y, double maxDistance, long nowMs, out AnimalPose animal)
    {
        double best = maxDistance * maxDistance;
        animal = default;
        bool found = false;
        for (int i = 0; i < _animals.Count; i++)
        {
            AnimalPose pose = _animals[i].PoseAt(nowMs);
            double dx = pose.X - x;
            double dy = pose.Y - y;
            double squared = (dx * dx) + (dy * dy);
            if (squared <= best)
            {
                best = squared;
                animal = pose;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Returns the name of an animal of the building the player is inside.</summary>
    /// <param name="animalId">The animal.</param>
    /// <returns>Its name, or an empty string when it is not here.</returns>
    public string NameOf(Guid animalId)
    {
        lock (_gate)
        {
            foreach (AnimalState animal in _animals)
            {
                if (animal.Id == animalId)
                {
                    return animal.Name;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Asks for the buildings of the land the player stands on.</summary>
    public void LoadHere() => _ = this.LoadBuildingsAsync(mine: false);

    /// <summary>Asks for the player's own buildings (the carpenter page).</summary>
    public void LoadMine() => _ = this.LoadBuildingsAsync(mine: true);

    /// <summary>Asks for the animals of the building the player stands in.</summary>
    public void LoadRoom() => _ = this.LoadRoomAsync();

    /// <summary>Forgets the room: the player left the building.</summary>
    public void ClearRoom()
    {
        lock (_gate)
        {
            if (_roomId == Guid.Empty && _animals.Count == 0)
            {
                return;
            }

            _roomId = Guid.Empty;
            _roomKind = 0;
            _rations = 0;
            _mayTend = false;
            _animals.Clear();
            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

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

    /// <summary>Puts feed from an inventory slot into the feeding bench.</summary>
    /// <param name="slot">The inventory slot.</param>
    /// <param name="quantity">How many.</param>
    public void Feed(int slot, int quantity) => _ = this.FeedAsync(slot, quantity);

    /// <summary>Pets an animal.</summary>
    /// <param name="animalId">The animal.</param>
    public void Pet(Guid animalId) => _ = this.PetAsync(animalId);

    /// <summary>Collects an animal's produce.</summary>
    /// <param name="animalId">The animal.</param>
    /// <param name="slot">The inventory slot of the tool in hand.</param>
    public void Collect(Guid animalId, int slot) => _ = this.CollectAsync(animalId, slot);

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

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next map change; there is no caller to report to.")]
    private async Task LoadRoomAsync()
    {
        try
        {
            using PacketScope<GetAnimals> request = PacketFactory<GetAnimals>.Acquire();
            AnimalsSnapshot snapshot = await _socket.RequestAsync<AnimalsSnapshot>(request.Value).ConfigureAwait(false);
            long now = this.NowMs;
            lock (_gate)
            {
                _animals.Clear();
                _roomId = snapshot.BuildingId;
                _roomKind = snapshot.BuildingId == Guid.Empty ? (byte)0 : snapshot.Kind;
                _rations = snapshot.Rations;
                _mayTend = snapshot.MayTend;
                foreach (AnimalRow row in snapshot.Animals)
                {
                    _animals.Add(AnimalState.From(row, now));
                }

                _ = Interlocked.Increment(ref _version);
            }

            this.Changed?.Invoke();
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

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task FeedAsync(int slot, int quantity)
    {
        try
        {
            using PacketScope<FeedAnimals> request = PacketFactory<FeedAnimals>.Acquire();
            request.Value.Slot = slot;
            request.Value.Quantity = quantity;
            BuildingResult result = await _socket.RequestAsync<BuildingResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new BuildingResultInfo(BuildingAction.Feed, result.Status, result.ItemId, 0, Guid.Empty));
        }
        catch (Exception)
        {
            // Connection dropped; nothing was fed.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task PetAsync(Guid animalId)
    {
        try
        {
            using PacketScope<PetAnimal> request = PacketFactory<PetAnimal>.Acquire();
            request.Value.AnimalId = animalId;
            AnimalResult result = await _socket.RequestAsync<AnimalResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new BuildingResultInfo(BuildingAction.Pet, result.Status, 0, result.Friendship, animalId));
        }
        catch (Exception)
        {
            // Connection dropped; nothing was petted.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task CollectAsync(Guid animalId, int slot)
    {
        try
        {
            using PacketScope<CollectProduce> request = PacketFactory<CollectProduce>.Acquire();
            request.Value.AnimalId = animalId;
            request.Value.Slot = slot;
            AnimalResult result = await _socket.RequestAsync<AnimalResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new BuildingResultInfo(BuildingAction.Collect, result.Status, result.ItemId, 0, animalId));
        }
        catch (Exception)
        {
            // Connection dropped; nothing was collected.
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

    private void OnAnimalsChanged(AnimalsChanged packet)
    {
        long now = this.NowMs;
        lock (_gate)
        {
            if (packet.BuildingId != _roomId)
            {
                return;
            }

            _rations = packet.Rations;
            foreach (AnimalRow row in packet.Upserted)
            {
                AnimalState? known = _animals.Find(a => a.Id == row.Id);
                if (known is null)
                {
                    _animals.Add(AnimalState.From(row, now));
                }
                else
                {
                    known.Update(row);
                }
            }

            foreach (Guid id in packet.Removed)
            {
                _ = _animals.RemoveAll(a => a.Id == id);
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnAnimalsMoved(AnimalsMoved packet)
    {
        long now = this.NowMs;
        lock (_gate)
        {
            if (packet.BuildingId != _roomId)
            {
                return;
            }

            foreach (AnimalMoveRow move in packet.Moves)
            {
                _animals.Find(a => a.Id == move.Id)?.Walk(move, now);
            }

            _ = Interlocked.Increment(ref _version);
        }
    }

    // One animal: its card and the straight walk it is on; where it is now is computed from the walk's start, never stored.
    private sealed class AnimalState
    {
        private double _fromX;
        private double _fromY;
        private double _toX;
        private double _toY;
        private long _startMs;
        private int _durationMs;
        private int _facing;

        public Guid Id { get; private init; }

        public byte Kind { get; private init; }

        public string Name { get; private set; } = string.Empty;

        public int Friendship { get; private set; }

        public byte Flags { get; private set; }

        public static AnimalState From(AnimalRow row, long nowMs)
        {
            AnimalState state = new() { Id = row.Id, Kind = row.Kind };
            state.Update(row);
            state._fromX = row.X;
            state._fromY = row.Y;
            state._toX = row.TargetX;
            state._toY = row.TargetY;
            state._startMs = nowMs;
            state._durationMs = row.MoveMs;
            state._facing = FacingOf(row.TargetX - row.X, row.TargetY - row.Y, 0);
            return state;
        }

        public void Update(AnimalRow row)
        {
            this.Name = row.Name;
            this.Friendship = row.Friendship;
            this.Flags = row.Flags;
        }

        public void Walk(AnimalMoveRow move, long nowMs)
        {
            _fromX = move.FromX;
            _fromY = move.FromY;
            _toX = move.ToX;
            _toY = move.ToY;
            _startMs = nowMs;
            _durationMs = move.DurationMs;
            _facing = FacingOf(move.ToX - move.FromX, move.ToY - move.FromY, _facing);
        }

        public AnimalPose PoseAt(long nowMs)
        {
            bool walking = _durationMs > 0 && nowMs < _startMs + _durationMs;
            double t = walking ? Math.Max(0, (nowMs - _startMs) / (double)_durationMs) : 1;
            return new AnimalPose(
                this.Id,
                this.Kind,
                this.Name,
                this.Friendship,
                (this.Flags & 1) != 0,
                (this.Flags & 2) != 0,
                _fromX + ((_toX - _fromX) * t),
                _fromY + ((_toY - _fromY) * t),
                _facing,
                walking);
        }

        // 0 down, 1 right, 2 up, 3 left: the larger axis of the walk wins.
        private static int FacingOf(double dx, double dy, int fallback)
        {
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
            {
                return fallback;
            }

            return Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 0 : 2);
        }
    }
}
