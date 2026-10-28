// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;

namespace Nosum.Wasm.Socket.Combat;

/// <summary>Client-side snapshot of one monster on the active map.</summary>
/// <param name="MonsterId">Monster id within the map/floor.</param>
/// <param name="Kind">Monster species byte (<c>MonsterKind</c>).</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
public readonly record struct MonsterClientRow(int MonsterId, byte Kind, int X, int Y, int Hp, int MaxHp);

/// <summary>Outcome of a client attack intent.</summary>
/// <param name="Status">Combat status.</param>
/// <param name="MonsterId">Target monster id.</param>
/// <param name="DamageDealt">Damage dealt to the monster.</param>
/// <param name="MonsterHp">Remaining monster HP.</param>
/// <param name="DamageTaken">Counter-attack damage taken by the player.</param>
/// <param name="KnockedOut">Whether the player was knocked out and revived.</param>
public readonly record struct AttackInfo(
    CombatStatus Status,
    int MonsterId,
    int DamageDealt,
    int MonsterHp,
    int DamageTaken,
    bool KnockedOut);

/// <summary>Active in-flight spell or ranged projectile (docs/adr/ADR-048).</summary>
/// <param name="StartX">Origin tile X.</param>
/// <param name="StartY">Origin tile Y.</param>
/// <param name="TargetX">Destination tile X.</param>
/// <param name="TargetY">Destination tile Y.</param>
/// <param name="StartTime">Timestamp in milliseconds.</param>
/// <param name="DurationMs">Flight duration in milliseconds.</param>
/// <param name="Kind">Projectile kind (1 = Arcane, 2 = Fire, 3 = Spore).</param>
public readonly record struct ProjectileClientRow(
    float StartX,
    float StartY,
    float TargetX,
    float TargetY,
    long StartTime,
    float DurationMs,
    byte Kind);

/// <summary>
/// Client mirror of player HP &amp; MP vitals, Combat XP, and live monsters on the active map (docs/adr/ADR-045 D3).
/// </summary>
public sealed class CombatChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly object _gate = new();
    private readonly List<MonsterClientRow> _monsters = [];
    private readonly List<ProjectileClientRow> _projectiles = [];

    private int _hp = 100;
    private int _maxHp = 100;
    private int _mp = 50;
    private int _maxMp = 50;
    private int _combatXp;
    private string _mapId = string.Empty;

    /// <summary>Initializes the combat channel.</summary>
    /// <param name="socket">The game connection.</param>
    public CombatChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _subscriptions =
        [
            socket.Subscribe<VitalsState>(this.OnVitals),
            socket.Subscribe<MonstersSnapshot>(this.OnMonstersSnapshot),
            socket.Subscribe<MonsterChanged>(this.OnMonsterChanged),
        ];
    }

    /// <summary>Raised when HP, MP or Combat XP changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the monster list on the current map changes.</summary>
    public event Action? MonstersUpdated;

    /// <summary>Raised when an attack completes.</summary>
    public event Action<AttackInfo>? AttackCompleted;

    /// <summary>Raised when a potion drink request completes.</summary>
    public event Action<CombatStatus>? PotionCompleted;

    /// <summary>Gets current HP.</summary>
    public int Hp => Volatile.Read(ref _hp);

    /// <summary>Gets maximum HP.</summary>
    public int MaxHp => Volatile.Read(ref _maxHp);

    /// <summary>Gets current MP.</summary>
    public int Mp => Volatile.Read(ref _mp);

    /// <summary>Gets maximum MP.</summary>
    public int MaxMp => Volatile.Read(ref _maxMp);

    /// <summary>Gets cumulative Combat XP.</summary>
    public int CombatXp => Volatile.Read(ref _combatXp);

    /// <summary>Gets the map id of the current monster roster.</summary>
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

    /// <summary>Copies the live monsters into <paramref name="destination"/> without allocating.</summary>
    /// <param name="destination">Destination list to populate.</param>
    public void CopyMonsters(List<MonsterClientRow> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.Hp > 0)
                {
                    destination.Add(row);
                }
            }
        }
    }

    /// <summary>Finds the nearest live monster within <paramref name="maxRange"/> tiles of <c>(tileX, tileY)</c>.</summary>
    /// <param name="tileX">Player tile X.</param>
    /// <param name="tileY">Player tile Y.</param>
    /// <param name="maxRange">Maximum Chebyshev tile distance.</param>
    /// <param name="monster">The nearest monster when found.</param>
    /// <returns><see langword="true"/> when a live monster is in range.</returns>
    public bool TryFindTarget(int tileX, int tileY, int maxRange, out MonsterClientRow monster)
    {
        lock (_gate)
        {
            int bestDist = int.MaxValue;
            MonsterClientRow best = default;
            bool found = false;
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.Hp <= 0)
                {
                    continue;
                }

                int dist = Math.Max(Math.Abs(row.X - tileX), Math.Abs(row.Y - tileY));
                if (dist <= maxRange && dist < bestDist)
                {
                    bestDist = dist;
                    best = row;
                    found = true;
                }
            }

            monster = best;
            return found;
        }
    }

    /// <summary>Requests current player vitals from the server.</summary>
    public void LoadVitals() => _ = this.LoadVitalsAsync();

    /// <summary>Requests current map monsters from the server.</summary>
    public void LoadMonsters() => _ = this.LoadMonstersAsync();

    /// <summary>Attacks <paramref name="monsterId"/> using the weapon/staff in <paramref name="slot"/>.</summary>
    /// <param name="monsterId">Target monster id.</param>
    /// <param name="slot">Hotbar/inventory slot.</param>
    public void Attack(int monsterId, int slot) => _ = this.AttackAsync(monsterId, slot);

    /// <summary>Drinks a potion in <paramref name="slot"/>.</summary>
    /// <param name="slot">Hotbar/inventory slot.</param>
    public void DrinkPotion(int slot) => _ = this.DrinkPotionAsync(slot);

    /// <summary>Spawns a client-side magic projectile along a trajectory (docs/adr/ADR-048).</summary>
    /// <param name="fromX">Starting tile X.</param>
    /// <param name="fromY">Starting tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <param name="kind">Projectile visual kind.</param>
    /// <param name="durationMs">Travel duration in ms.</param>
    public void SpawnProjectile(float fromX, float fromY, float toX, float toY, byte kind = 1, float durationMs = 260f)
    {
        lock (_gate)
        {
            if (_projectiles.Count >= 32)
            {
                _projectiles.RemoveAt(0);
            }

            _projectiles.Add(new ProjectileClientRow(fromX, fromY, toX, toY, Environment.TickCount64, durationMs, kind));
        }
    }

    /// <summary>Copies currently active or impacting projectiles without heap allocation.</summary>
    /// <param name="destination">Destination list to populate.</param>
    /// <param name="now">Current tick timestamp.</param>
    public void CopyProjectiles(List<ProjectileClientRow> destination, long now)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                ProjectileClientRow p = _projectiles[i];
                if (now - p.StartTime > p.DurationMs + 200)
                {
                    _projectiles.RemoveAt(i);
                }
                else
                {
                    destination.Add(p);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable sub in _subscriptions)
        {
            sub.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; next action retries.")]
    private async Task LoadVitalsAsync()
    {
        try
        {
            using PacketScope<GetVitals> req = PacketFactory<GetVitals>.Acquire();
            VitalsState reply = await _socket.RequestAsync<VitalsState>(req.Value).ConfigureAwait(false);
            this.OnVitals(reply);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; next map load retries.")]
    private async Task LoadMonstersAsync()
    {
        try
        {
            using PacketScope<GetMonsters> req = PacketFactory<GetMonsters>.Acquire();
            MonstersSnapshot reply = await _socket.RequestAsync<MonstersSnapshot>(req.Value).ConfigureAwait(false);
            this.OnMonstersSnapshot(reply);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; player can swing again.")]
    private async Task AttackAsync(int monsterId, int slot)
    {
        try
        {
            using PacketScope<AttackMonster> req = PacketFactory<AttackMonster>.Acquire();
            req.Value.MonsterId = monsterId;
            req.Value.Slot = (byte)slot;
            AttackMonsterResult reply = await _socket.RequestAsync<AttackMonsterResult>(req.Value).ConfigureAwait(false);
            if (reply.MaxHp > 0)
            {
                Volatile.Write(ref _hp, reply.Hp);
                Volatile.Write(ref _maxHp, reply.MaxHp);
                Volatile.Write(ref _mp, reply.Mp);
                Volatile.Write(ref _maxMp, reply.MaxMp);
                Volatile.Write(ref _combatXp, reply.CombatXp);
                this.Changed?.Invoke();
            }

            if (reply.Status == CombatStatus.Ok)
            {
                this.UpdateMonsterHp(reply.MonsterId, reply.MonsterHp);
            }

            this.AttackCompleted?.Invoke(new AttackInfo(
                reply.Status,
                reply.MonsterId,
                reply.DamageDealt,
                reply.MonsterHp,
                reply.DamageTaken,
                reply.KnockedOut));
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored.")]
    private async Task DrinkPotionAsync(int slot)
    {
        try
        {
            using PacketScope<DrinkPotion> req = PacketFactory<DrinkPotion>.Acquire();
            req.Value.Slot = (byte)slot;
            DrinkPotionResult reply = await _socket.RequestAsync<DrinkPotionResult>(req.Value).ConfigureAwait(false);
            if (reply.MaxHp > 0)
            {
                Volatile.Write(ref _hp, reply.Hp);
                Volatile.Write(ref _maxHp, reply.MaxHp);
                Volatile.Write(ref _mp, reply.Mp);
                Volatile.Write(ref _maxMp, reply.MaxMp);
                this.Changed?.Invoke();
            }

            this.PotionCompleted?.Invoke(reply.Status);
        }
        catch (Exception)
        {
        }
    }

    private void OnVitals(VitalsState packet)
    {
        if (packet.MaxHp <= 0)
        {
            return;
        }

        Volatile.Write(ref _hp, packet.Hp);
        Volatile.Write(ref _maxHp, packet.MaxHp);
        Volatile.Write(ref _mp, packet.Mp);
        Volatile.Write(ref _maxMp, packet.MaxMp);
        Volatile.Write(ref _combatXp, packet.CombatXp);
        this.Changed?.Invoke();
    }

    private void OnMonstersSnapshot(MonstersSnapshot packet)
    {
        lock (_gate)
        {
            _mapId = packet.MapId ?? string.Empty;
            _monsters.Clear();
            foreach (MonsterRow row in packet.Monsters)
            {
                if (row.Hp > 0)
                {
                    _monsters.Add(new MonsterClientRow(row.MonsterId, row.Kind, row.X, row.Y, row.Hp, row.MaxHp));
                }
            }
        }

        this.MonstersUpdated?.Invoke();
    }

    private void OnMonsterChanged(MonsterChanged packet)
    {
        lock (_gate)
        {
            bool found = false;
            for (int i = 0; i < _monsters.Count; i++)
            {
                if (_monsters[i].MonsterId == packet.MonsterId)
                {
                    if (packet.Hp == 0)
                    {
                        _monsters.RemoveAt(i);
                    }
                    else
                    {
                        _monsters[i] = new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp);
                    }

                    found = true;
                    break;
                }
            }

            if (!found && packet.Hp > 0)
            {
                _monsters.Add(new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp));
            }
        }

        this.MonstersUpdated?.Invoke();
    }

    private void UpdateMonsterHp(int monsterId, int remainingHp)
    {
        lock (_gate)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                if (_monsters[i].MonsterId == monsterId)
                {
                    if (remainingHp <= 0)
                    {
                        _monsters.RemoveAt(i);
                    }
                    else
                    {
                        _monsters[i] = _monsters[i] with { Hp = remainingHp };
                    }

                    break;
                }
            }
        }

        this.MonstersUpdated?.Invoke();
    }
}
