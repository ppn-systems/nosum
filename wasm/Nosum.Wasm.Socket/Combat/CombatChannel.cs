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
using Nosum.Gameplay.Combat;

namespace Nosum.Wasm.Socket.Combat;

/// <summary>
/// Client mirror of player HP &amp; MP vitals, Combat XP, and live monsters on the active map (docs/adr/ADR-045 D3).
/// </summary>
public sealed partial class CombatChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<MonsterClientRow> _monsters = [];
    private readonly List<ProjectileClientRow> _projectiles = [];
    private readonly List<DeathEffectClientRow> _deathEffects = [];
    private readonly List<GroundTelegraphClientRow> _telegraphs = [];
    private readonly List<FloatingTextClientRow> _floatingTexts = [];
    private readonly List<HazardClientRow> _hazards = [];
    private readonly Dictionary<byte, long> _skillReadyAt = new();
    // The length of the cooldown each skill's sweep runs over: the full cooldown when it was cast, the time left when it was refused as cooling down.
    private readonly Dictionary<byte, int> _skillSpanMs = new();
    // One bit per skill id (1 to 3) while its cast is on its way to the server and not answered yet (docs/adr/ADR-056 D5).
    private int _skillPending;
    private int _playerX;
    private int _playerY;
    private byte _playerFacing;
    private int _activeSkillId;
    private long _activeSkillTick;

    /// <summary>The combo streak length that lands the finisher burst and thump (docs/adr/ADR-060).</summary>
    public const int FinisherComboCount = 5;

    /// <summary>How long the finisher burst stays on screen, in milliseconds.</summary>
    public const int FinisherDurationMs = 700;

    /// <summary>Impact of a landed hit: a light strike, the heavy strike whose damage number is crit-styled, or the combo finisher (docs/adr/ADR-076).</summary>
    public const int ImpactLight = 0;

    /// <summary>See <see cref="ImpactLight"/>.</summary>
    public const int ImpactHeavy = 1;

    /// <summary>See <see cref="ImpactLight"/>.</summary>
    public const int ImpactFinisher = 2;

    private int _hp = 100;

    // The last damage number shown for the farmer: the HP after that hit, its amount and when it showed (docs/adr/ADR-055 amendment).
    private const long DuplicateHitWindowMs = 1500;
    private int _lastHitHpAfter = -1;
    private int _lastHitAmount;
    private long _lastHitShownAt;
    private int _maxHp = 100;
    private int _mp = 50;
    private int _maxMp = 50;
    private int _combatXp;
    private int _statusEffect;
    private int _buff;
    private int _comboCount;
    private long _comboExpireTick;
    private bool _hasInitialVitals;
    private int _lastCelebratedLevel;
    private int _levelUpValue;
    private long _levelUpTick;
    private long _finisherTick;
    private int _finisherTileX;
    private int _finisherTileY;
    private int _hitMonsterId;
    private long _hitTick;
    private int _hitImpact;
    private int _slideMonsterId;
    private int _slideFromX;
    private int _slideFromY;
    private long _slideTick;
    private long _shakeTick;
    private int _shakeAmplitude;
    private int _shakeDurationMs;
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
            socket.Subscribe<GroundTelegraph>(this.OnGroundTelegraph),
        ];
    }

    /// <summary>Invoked when the player's active combo streak changes (docs/adr/ADR-060).</summary>
    public event Action? ComboChanged;

    /// <summary>Invoked when a combo streak reaches the finisher length (docs/adr/ADR-060).</summary>
    public event Action? ComboFinished;

    /// <summary>Invoked when the player reaches a new Combat level (docs/adr/ADR-062).</summary>
    public event Action<int>? LevelledUp;

    /// <summary>Raised when HP, MP or Combat XP changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the monster list on the current map changes.</summary>
    public event Action? MonstersUpdated;

    /// <summary>Raised when an attack completes.</summary>
    public event Action<AttackInfo>? AttackCompleted;

    /// <summary>Raised when a potion drink request completes.</summary>
    public event Action<CombatStatus>? PotionCompleted;

    /// <summary>Raised when a skill cast request completes (docs/adr/ADR-056).</summary>
    public event Action<SkillInfo>? SkillCompleted;

    /// <summary>Raised when the player is knocked out and revived at the farmhouse.</summary>
    public event Action? KnockedOut;

    /// <summary>Gets whether the celebratory level-up radiance banner is currently active.</summary>
    public bool IsLevelUpActive => Volatile.Read(ref _levelUpValue) > 0 && (Environment.TickCount64 - Volatile.Read(ref _levelUpTick)) < 3800;

    /// <summary>Gets the level currently being celebrated.</summary>
    public int ActiveLevelUpValue => Volatile.Read(ref _levelUpValue);

    /// <summary>Gets the current active combat combo count.</summary>
    public int CurrentCombo
    {
        get
        {
            if (Environment.TickCount64 > Volatile.Read(ref _comboExpireTick))
            {
                return 0;
            }

            return Volatile.Read(ref _comboCount);
        }
    }

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

    /// <summary>Gets the active status effect kind (0 = None, 1 = Regen, 2 = Poison, docs/adr/ADR-051).</summary>
    public int StatusEffect => Volatile.Read(ref _statusEffect);

    /// <summary>Gets the active speed buff kind in its own slot (0 = None, 3 = Swiftness, 4 = Brotherhood in Arms; issue #146).</summary>
    public int Buff => Volatile.Read(ref _buff);

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

    /// <summary>Copies the active boss ground slam telegraphs into <paramref name="destination"/> without allocating.</summary>
    /// <param name="destination">Destination list to populate.</param>
    public void CopyGroundTelegraphs(List<GroundTelegraphClientRow> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            destination.AddRange(_telegraphs);
        }
    }

    /// <summary>Copies active environmental hazards into <paramref name="destination"/> without allocating.</summary>
    /// <param name="destination">Destination list to populate.</param>
    public void CopyHazards(List<HazardClientRow> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            destination.AddRange(_hazards);
        }
    }

    /// <summary>Gets the tile of a living monster on the current map, for the tester's teleport next to one (docs/adr/ADR-079).</summary>
    /// <param name="tileX">The monster's tile column.</param>
    /// <param name="tileY">The monster's tile row.</param>
    /// <returns><see langword="true"/> when a living monster is known.</returns>
    public bool TryGetLivingMonsterTile(out int tileX, out int tileY)
    {
        lock (_gate)
        {
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.Hp > 0)
                {
                    tileX = row.X;
                    tileY = row.Y;
                    return true;
                }
            }
        }

        tileX = 0;
        tileY = 0;
        return false;
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

    /// <summary>Finds a monster by its id.</summary>
    /// <param name="monsterId">Monster id to find.</param>
    /// <param name="monster">The monster if found.</param>
    /// <returns><see langword="true"/> when the monster is present.</returns>
    public bool TryGetMonster(int monsterId, out MonsterClientRow monster)
    {
        lock (_gate)
        {
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.MonsterId == monsterId)
                {
                    monster = row;
                    return true;
                }
            }

            monster = default;
            return false;
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

    /// <summary>Gets the remaining cooldown in seconds for a skill (0 when ready) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">The skill kind.</param>
    /// <returns>Seconds remaining until ready.</returns>
    public float GetSkillCooldownRemaining(byte skillId)
    {
        lock (_gate)
        {
            if (_skillReadyAt.TryGetValue(skillId, out long readyAt))
            {
                long diff = readyAt - Environment.TickCount64;
                return diff > 0 ? diff / 1000f : 0f;
            }

            return 0f;
        }
    }

    /// <summary>
    /// Gets the length of the cooldown a skill's sweep runs over (docs/adr/ADR-056 D5). It is the full cooldown when the cast landed and the time left
    /// when the server refused the cast as cooling down, so the sweep ends when the server's cooldown does.
    /// </summary>
    /// <param name="skillId">The skill kind.</param>
    /// <returns>Milliseconds; zero when no cooldown was started.</returns>
    public int GetSkillCooldownSpanMs(byte skillId)
    {
        lock (_gate)
        {
            return _skillSpanMs.TryGetValue(skillId, out int span) ? span : 0;
        }
    }

    /// <summary>Returns whether the given skill is off cooldown and the player has enough MP to cast it.</summary>
    /// <param name="skillId">Skill kind.</param>
    /// <returns><see langword="true"/> if the skill can be cast right now.</returns>
    public bool CanCastSkill(byte skillId)
    {
        if (this.IsSkillPending(skillId) || this.GetSkillCooldownRemaining(skillId) > 0f)
        {
            return false;
        }

        return !SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition def) || this.Mp >= def.MpCost;
    }

    /// <summary>Casts a combat skill (1 = Whirlwind, 2 = Fireball, 3 = DivineHeal) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">Skill kind.</param>
    /// <param name="targetMonsterId">Target monster id (0 for self/AoE, or auto-targets nearest within range 6).</param>
    public void CastSkill(byte skillId, int targetMonsterId = 0)
    {
        if (!this.CanCastSkill(skillId))
        {
            return;
        }

        // The effect and the cooldown wait for the server's answer: a refused cast shows nothing (docs/adr/ADR-056 D5).
        lock (_gate)
        {
            _skillPending |= 1 << skillId;
        }

        if (skillId == 2 && targetMonsterId == 0)
        {
            if (this.TryFindTarget(_playerX, _playerY, 6, out MonsterClientRow m))
            {
                targetMonsterId = m.MonsterId;
            }
        }

        _ = this.CastSkillAsync(skillId, targetMonsterId);
    }

    /// <summary>Gets whether a cast of the skill has been sent and not answered yet; a press meanwhile does nothing (docs/adr/ADR-056 D5).</summary>
    /// <param name="skillId">Skill kind.</param>
    /// <returns><see langword="true"/> while the reply is on its way.</returns>
    public bool IsSkillPending(byte skillId)
    {
        lock (_gate)
        {
            return (_skillPending & (1 << skillId)) != 0;
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
                this.CheckCombatLevelUp(reply.CombatXp);
                this.Changed?.Invoke();
            }

            if (reply.Status == CombatStatus.Ok)
            {
                if (reply.ComboCount > 0)
                {
                    this.RecordCombo(reply.ComboCount, reply.MonsterId);
                }

                // The damage number's style and the impact feel read the same rule, so a crit-styled number always shakes the screen.
                byte style = (byte)(reply.DamageDealt >= 18 || reply.ComboCount >= 3 ? 1 : 0);
                int impact = reply.ComboCount == FinisherComboCount ? ImpactFinisher : style == 1 ? ImpactHeavy : ImpactLight;
                if (this.TryGetMonster(reply.MonsterId, out MonsterClientRow m))
                {
                    this.SpawnFloatingText((m.X * 16) + 8, (m.Y * 16) - 6, reply.DamageDealt, style);
                }

                this.AnnouncePlayerDamage(reply.Hp, reply.DamageTaken, 2);

                this.RecordMonsterHit(reply.MonsterId, impact);
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
                int oldHp = Volatile.Read(ref _hp);
                int hpDiff = reply.Hp - oldHp;
                if (hpDiff > 0)
                {
                    _lastHitHpAfter = -1;
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, hpDiff, 3);
                }

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

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored.")]
    private async Task CastSkillAsync(byte skillId, int targetMonsterId)
    {
        try
        {
            using PacketScope<CastSkill> req = PacketFactory<CastSkill>.Acquire();
            req.Value.SkillId = skillId;
            req.Value.TargetMonsterId = targetMonsterId;
            CastSkillResult reply = await _socket.RequestAsync<CastSkillResult>(req.Value).ConfigureAwait(false);
            this.ClearSkillPending(skillId);

            if (reply.CooldownMs > 0)
            {
                long readyAt = Environment.TickCount64 + reply.CooldownMs;
                lock (_gate)
                {
                    _skillReadyAt[reply.SkillId] = readyAt;
                    _skillSpanMs[reply.SkillId] = reply.CooldownMs;
                }

                _ = this.ExpireSkillCooldownAsync(reply.SkillId, reply.CooldownMs, readyAt);
            }

            if (reply.Status == CombatStatus.Ok)
            {
                this.TriggerSkillEffect(reply.SkillId, targetMonsterId);
                if (reply.SkillId == 3 && reply.HealAmount > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, reply.HealAmount, 3);
                }
                else if (reply.SkillId == 2 && reply.DamageDealt > 0)
                {
                    if (this.TryGetMonster(targetMonsterId, out MonsterClientRow tm))
                    {
                        this.SpawnFloatingText((tm.X * 16) + 8, (tm.Y * 16) - 6, reply.DamageDealt, 1);
                    }
                }
                else if (reply.SkillId == 1 && reply.DamageDealt > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 10, reply.DamageDealt, 1);
                }

                if (reply.ComboCount > 0)
                {
                    this.RecordCombo(reply.ComboCount, targetMonsterId);
                }
            }

            if (reply.MaxHp > 0)
            {
                Volatile.Write(ref _hp, reply.Hp);
                Volatile.Write(ref _maxHp, reply.MaxHp);
                Volatile.Write(ref _mp, reply.Mp);
                Volatile.Write(ref _maxMp, reply.MaxMp);
                Volatile.Write(ref _combatXp, reply.CombatXp);
                this.CheckCombatLevelUp(reply.CombatXp);
            }

            this.Changed?.Invoke();

            this.SkillCompleted?.Invoke(new SkillInfo(
                reply.Status,
                reply.SkillId,
                reply.DamageDealt,
                reply.TargetsHit,
                reply.HealAmount,
                reply.CooldownMs));
        }
        catch (Exception)
        {
            this.ClearSkillPending(skillId);
        }
    }

    // Cleared before the reply's Changed, so the buttons re-enable on a refusal too.
    private void ClearSkillPending(byte skillId)
    {
        lock (_gate)
        {
            _skillPending &= ~(1 << skillId);
        }
    }

    private void OnMonstersSnapshot(MonstersSnapshot packet)
    {
        lock (_gate)
        {
            string incomingMapId = packet.MapId ?? string.Empty;
            bool mapChanged = !string.Equals(_mapId, incomingMapId, StringComparison.Ordinal);
            _mapId = incomingMapId;
            if (mapChanged)
            {
                _projectiles.Clear();
                _deathEffects.Clear();
                _telegraphs.Clear();
                _floatingTexts.Clear();
                Volatile.Write(ref _activeSkillId, 0);
                Volatile.Write(ref _activeSkillTick, 0);
            }

            _monsters.Clear();
            foreach (MonsterRow row in packet.Monsters)
            {
                if (row.Hp > 0)
                {
                    _monsters.Add(new MonsterClientRow(row.MonsterId, row.Kind, row.X, row.Y, row.Hp, row.MaxHp, row.Modifier, row.Enraged != 0));
                }
            }

            _hazards.Clear();
            if (packet.Hazards is not null)
            {
                foreach (HazardRow h in packet.Hazards)
                {
                    _hazards.Add(new HazardClientRow(h.HazardId, h.Kind, h.X, h.Y, h.Damage));
                }
            }
        }

        this.MonstersUpdated?.Invoke();
    }
}
