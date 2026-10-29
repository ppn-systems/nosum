// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;

namespace Nosum.Wasm.Socket.Building;

/// <summary>
/// Animal room state, walk interpolation, and tending commands for <see cref="BuildingChannel"/>.
/// </summary>
public sealed partial class BuildingChannel
{
    /// <summary>Gets how many animals the building the player is inside holds. Read under <see cref="Read"/>.</summary>
    public int AnimalCount => _animals.Count;

    /// <summary>Returns an animal with its position at a time. Read under <see cref="Read"/>; allocates nothing.</summary>
    /// <param name="index">Zero to <see cref="AnimalCount"/> minus one.</param>
    /// <param name="nowMs">The time, from <see cref="NowMs"/>.</param>
    /// <returns>The animal.</returns>
    public AnimalPose AnimalAt(int index, long nowMs) => _animals[index].PoseAt(nowMs);

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
