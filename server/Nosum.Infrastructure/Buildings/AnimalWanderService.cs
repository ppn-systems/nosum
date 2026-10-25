// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Building.S2C;
using Nosum.Domain.Buildings;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Buildings;

/// <summary>
/// <see cref="IAnimalWander"/> (docs/adr/ADR-028 D4): each animal of a building with a player inside is on a straight walk the server chose, and when it is due (a few seconds after it
/// arrived) picks the next one inside the building's animal area on tiles it can walk straight to. The walks are published to the players inside the building through one multicast
/// (<see cref="PublishAsync"/>), so a player outside costs nothing. A walk is stored as a start time and a duration, never advanced by a timer: where an animal is, is computed when asked.
/// </summary>
public sealed class AnimalWanderService : IAnimalWander
{
    private const int MinPauseMs = 2_000;
    private const int MaxPauseMs = 5_000;
    private const int MaxWalkTiles = 6;
    private const int Attempts = 8;

    private readonly IMapPresence _presence;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly IBuildingDirectory _buildings;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ConcurrentDictionary<Guid, Walk> _walks = new();
    private readonly TimeProvider _time;
    private readonly GameRandom _random;
    private readonly Lock _randomGate = new();

    /// <summary>The time between two ticks of <see cref="RunAsync"/>: animals decide every few seconds, so one tick a second is plenty.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands in which building.</param>
    /// <param name="publisher">Tells the players in a building about the walks, one packet per chunk the walks start in (docs/adr/ADR-022 D4).</param>
    /// <param name="maps">The hosted maps: the interiors' walk grids.</param>
    /// <param name="buildings">Finds a building by the id in its instance key.</param>
    /// <param name="unitOfWork">Creates units of work: the animals of a building are read from the database.</param>
    /// <param name="time">The server clock the walks are stamped with: the one the building service reads, so a development clock that jumps days moves both.</param>
    /// <param name="seed">Seeds the choice of walks; tests pass a fixed one.</param>
    public AnimalWanderService(IMapPresence presence, IChunkPublisher publisher, IMapCatalog maps, IBuildingDirectory buildings, IUnitOfWorkFactory unitOfWork, TimeProvider time, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _presence = presence;
        _publisher = publisher;
        _maps = maps;
        _buildings = buildings;
        _unitOfWork = unitOfWork;
        _random = new GameRandom(seed);
    }

    /// <summary>Ticks every <paramref name="interval"/> until cancelled; a failed tick is reported and the next one tries again.</summary>
    /// <param name="interval">Time between ticks.</param>
    /// <param name="onError">Told about a failed tick.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "One failed tick must not stop the animals; the next tick retries.")]
    public async Task RunAsync(TimeSpan interval, Action<Exception> onError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onError);

        using PeriodicTimer timer = new(interval, TimeProvider.System);
        try
        {
            do
            {
                try
                {
                    await this.TickAsync(_time.GetUtcNow().ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onError(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <inheritdoc/>
    public async Task TickAsync(long nowMs, CancellationToken cancellationToken = default)
    {
        List<string> keys = [];
        _presence.CopyActiveMapIds(keys);
        List<PlayerPresence> players = [];
        foreach (string key in keys)
        {
            if (!TryParse(key, out string mapId, out Guid buildingId) || !BuildingCatalog.TryGetByInterior(mapId, out BuildingDefinition? definition))
            {
                continue;
            }

            players.Clear();
            _presence.CopyPlayers(key, players);
            if (players.Count == 0)
            {
                continue;
            }

            await this.StepAsync(buildingId, definition, new MapInstance(mapId, buildingId), nowMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public AnimalMotionView Where(BuildingKind kind, Guid animalId, long nowMs)
    {
        Walk walk = _walks.GetOrAdd(animalId, _ => this.Place(kind, animalId, nowMs));
        lock (walk)
        {
            return walk.At(nowMs);
        }
    }

    /// <inheritdoc/>
    public void Forget(Guid animalId) => _ = _walks.TryRemove(animalId, out _);

    // Tells the players inside a building about walks that were just chosen: one packet per chunk the walks start in (a barn is two chunks wide).
    private async Task PublishAsync(MapInstance instance, Guid buildingId, int tileSize, List<AnimalMoveRow> moves, CancellationToken cancellationToken)
    {
        Dictionary<ChunkCoord, List<AnimalMoveRow>> byChunk = [];
        foreach (AnimalMoveRow move in moves)
        {
            ChunkCoord chunk = ChunkCoord.OfPixels(move.FromX, move.FromY, tileSize);
            if (!byChunk.TryGetValue(chunk, out List<AnimalMoveRow>? group))
            {
                group = [];
                byChunk[chunk] = group;
            }

            group.Add(move);
        }

        foreach ((ChunkCoord chunk, List<AnimalMoveRow> group) in byChunk)
        {
            using PacketScope<AnimalsMoved> lease = PacketFactory<AnimalsMoved>.Acquire();
            AnimalsMoved packet = lease.Value;
            packet.BuildingId = buildingId;
            packet.Moves.Clear();
            packet.Moves.AddRange(group);
            await _publisher.PublishAsync(instance, chunk, packet, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryParse(string key, out string mapId, out Guid buildingId)
    {
        buildingId = Guid.Empty;
        int at = key.IndexOf('@', StringComparison.Ordinal);
        mapId = at > 0 ? key[..at] : string.Empty;
        return at > 0 && Guid.TryParseExact(key.AsSpan(at + 1), "N", out buildingId);
    }

    private async Task StepAsync(Guid buildingId, BuildingDefinition definition, MapInstance instance, long nowMs, CancellationToken cancellationToken)
    {
        if (!_maps.TryGet(definition.Key, out MapDefinition? map) || await _buildings.FindAsync(buildingId, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        List<(Guid Id, AnimalKind Kind)> animals = await this.AnimalsOfAsync(buildingId, cancellationToken).ConfigureAwait(false);
        List<AnimalMoveRow> moves = [];
        foreach ((Guid id, AnimalKind kind) in animals)
        {
            Walk walk = _walks.GetOrAdd(id, _ => this.Place(definition.Kind, id, nowMs));
            if (!AnimalCatalog.TryGet(kind, out AnimalDefinition? animal))
            {
                continue;
            }

            lock (walk)
            {
                if (nowMs >= walk.NextDecisionMs && this.TryChoose(walk, definition, map.Grid, animal.WalkPixelsPerSecond, nowMs))
                {
                    AnimalMoveRow row = AnimalMoveRow.Create();
                    row.Id = id;
                    row.FromX = walk.FromX;
                    row.FromY = walk.FromY;
                    row.ToX = walk.ToX;
                    row.ToY = walk.ToY;
                    row.DurationMs = walk.DurationMs;
                    moves.Add(row);
                }
            }
        }

        if (moves.Count > 0)
        {
            await this.PublishAsync(instance, buildingId, map.Grid.TileSize, moves, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<(Guid Id, AnimalKind Kind)>> AnimalsOfAsync(Guid buildingId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<FarmAnimal> rows = await unit.Repository<FarmAnimal>().ListAsync(a => a.BuildingId == buildingId, cancellationToken).ConfigureAwait(false);
            List<(Guid, AnimalKind)> animals = new(rows.Count);
            foreach (FarmAnimal row in rows)
            {
                animals.Add((row.Id, (AnimalKind)row.Kind));
            }

            return animals;
        }
    }

    // A new animal first stands on a walkable tile of its area, the same one for the same id.
    private Walk Place(BuildingKind kind, Guid animalId, long nowMs)
    {
        if (!BuildingCatalog.TryGet(kind, out BuildingDefinition? definition) || !_maps.TryGet(definition.Key, out MapDefinition? map))
        {
            return new Walk(0, 0, nowMs);
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = animalId.TryWriteBytes(bytes);
        GameRandom random = new(GameRandom.Mix(BitConverter.ToUInt64(bytes), 0x41_4E_49_4D));
        int tileSize = map.Grid.TileSize;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            int tx = definition.WanderLeft + random.Range(0, definition.WanderWidth - 1);
            int ty = definition.WanderTop + random.Range(0, definition.WanderHeight - 1);
            if (IsStandable(definition, map.Grid, tx, ty))
            {
                return new Walk(FeetX(tx, tileSize), FeetY(ty, tileSize), nowMs);
            }
        }

        return new Walk(FeetX(definition.ArrivalX, tileSize), FeetY(definition.ArrivalY, tileSize), nowMs);
    }

    // Chooses the next straight walk; false when nothing was found (the animal tries again in a moment).
    private bool TryChoose(Walk walk, BuildingDefinition definition, WalkGrid grid, int pixelsPerSecond, long nowMs)
    {
        AnimalMotionView here = walk.At(nowMs);
        int tileSize = grid.TileSize;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            int tx;
            int ty;
            int pause;
            lock (_randomGate)
            {
                tx = definition.WanderLeft + _random.Range(0, definition.WanderWidth - 1);
                ty = definition.WanderTop + _random.Range(0, definition.WanderHeight - 1);
                pause = _random.Range(MinPauseMs, MaxPauseMs);
            }

            float toX = FeetX(tx, tileSize);
            float toY = FeetY(ty, tileSize);
            float distance = MathF.Sqrt(((toX - here.X) * (toX - here.X)) + ((toY - here.Y) * (toY - here.Y)));
            if (distance < tileSize || distance > MaxWalkTiles * tileSize || !IsStandable(definition, grid, tx, ty) || !SegmentIsClear(definition, grid, here.X, here.Y, toX, toY))
            {
                continue;
            }

            int duration = (int)(distance * 1000 / Math.Max(1, pixelsPerSecond));
            walk.Start(here.X, here.Y, toX, toY, nowMs, duration, nowMs + duration + pause);
            return true;
        }

        walk.Wait(nowMs + MinPauseMs);
        return false;
    }

    private static float FeetX(int tileX, int tileSize) => (tileX * tileSize) + (tileSize / 2f);

    private static float FeetY(int tileY, int tileSize) => ((tileY + 1) * tileSize) - 2f;

    private static bool IsStandable(BuildingDefinition definition, WalkGrid grid, int tileX, int tileY)
        => definition.InWanderArea(tileX, tileY) && !definition.IsTrough(tileX, tileY) && !grid.IsBlocked(tileX, tileY);

    // Every few pixels of the line stay on tiles an animal may stand on, so it never cuts through the bench or a wall.
    private static bool SegmentIsClear(BuildingDefinition definition, WalkGrid grid, float fromX, float fromY, float toX, float toY)
    {
        float length = MathF.Sqrt(((toX - fromX) * (toX - fromX)) + ((toY - fromY) * (toY - fromY)));
        int steps = Math.Max(1, (int)(length / 4));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int tx = (int)MathF.Floor((fromX + ((toX - fromX) * t)) / grid.TileSize);
            int ty = (int)MathF.Floor((fromY + ((toY - fromY) * t)) / grid.TileSize);
            if (!IsStandable(definition, grid, tx, ty))
            {
                return false;
            }
        }

        return true;
    }

    // One animal's walk: where it started and ended, when, and when it decides again. Guarded by its own lock.
    private sealed class Walk
    {
        public Walk(float x, float y, long nowMs)
        {
            this.FromX = x;
            this.FromY = y;
            this.ToX = x;
            this.ToY = y;
            this.StartMs = nowMs;
            this.NextDecisionMs = nowMs + MinPauseMs;
        }

        public float FromX { get; private set; }

        public float FromY { get; private set; }

        public float ToX { get; private set; }

        public float ToY { get; private set; }

        public long StartMs { get; private set; }

        public int DurationMs { get; private set; }

        public long NextDecisionMs { get; private set; }

        public AnimalMotionView At(long nowMs)
        {
            if (this.DurationMs <= 0 || nowMs >= this.StartMs + this.DurationMs)
            {
                return new AnimalMotionView(this.ToX, this.ToY, this.ToX, this.ToY, 0);
            }

            float t = Math.Max(0f, (nowMs - this.StartMs) / (float)this.DurationMs);
            return new AnimalMotionView(
                this.FromX + ((this.ToX - this.FromX) * t),
                this.FromY + ((this.ToY - this.FromY) * t),
                this.ToX,
                this.ToY,
                (int)(this.StartMs + this.DurationMs - nowMs));
        }

        public void Start(float fromX, float fromY, float toX, float toY, long startMs, int durationMs, long nextDecisionMs)
        {
            this.FromX = fromX;
            this.FromY = fromY;
            this.ToX = toX;
            this.ToY = toY;
            this.StartMs = startMs;
            this.DurationMs = durationMs;
            this.NextDecisionMs = nextDecisionMs;
        }

        public void Wait(long nextDecisionMs) => this.NextDecisionMs = nextDecisionMs;
    }
}
