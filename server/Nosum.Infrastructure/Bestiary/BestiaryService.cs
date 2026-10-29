// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Combat;
using Nosum.Domain.Bestiary;
using Nosum.Gameplay.Combat;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Bestiary;

/// <summary>
/// <see cref="IBestiaryService"/>: one <see cref="PlayerBestiaryEntry"/> row per player and species in PostgreSQL, and at most one
/// <see cref="EquippedBestiaryTitle"/> per player. A count is a read-modify-write, so a player's kills and title changes run one at a time under a
/// per-player lock; the primary keys stay the backstop (docs/adr/ADR-068).
/// </summary>
public sealed class BestiaryService : IBestiaryService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _playerLocks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for the update timestamps; tests pass a fake one.</param>
    public BestiaryService(IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task RecordKillAsync(Guid playerId, MonsterKind species, CancellationToken cancellationToken = default)
    {
        if (species == MonsterKind.None)
        {
            return;
        }

        using (await _playerLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<PlayerBestiaryEntry> entries = unit.Repository<PlayerBestiaryEntry>();
                PlayerBestiaryEntry? entry = await entries.FirstOrDefaultAsync(e => e.PlayerId == playerId && e.Species == species, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                {
                    entries.Add(new PlayerBestiaryEntry { PlayerId = playerId, Species = species, Kills = 1, UpdatedAt = _time.GetUtcNow() });
                }
                else
                {
                    entry.Kills++;
                    entry.UpdatedAt = _time.GetUtcNow();
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<BestiaryView> ReadAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        using (await _playerLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                List<PlayerBestiaryEntry> entries = await unit.Repository<PlayerBestiaryEntry>().ListAsync(e => e.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
                EquippedBestiaryTitle? equipped = await unit.Repository<EquippedBestiaryTitle>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                List<BestiaryLine> lines = [.. entries.OrderBy(e => e.Species).Select(e => new BestiaryLine(e.Species, e.Kills))];
                return new BestiaryView(equipped?.Species ?? MonsterKind.None, lines);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<BestiaryStatus> EquipTitleAsync(Guid playerId, MonsterKind species, CancellationToken cancellationToken = default)
    {
        if (species != MonsterKind.None && !MonsterCatalog.TryGet(species, out _))
        {
            return BestiaryStatus.UnknownSpecies;
        }

        using (await _playerLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                int kills = 0;
                if (species != MonsterKind.None)
                {
                    kills = (await unit.Repository<PlayerBestiaryEntry>().FirstOrDefaultAsync(e => e.PlayerId == playerId && e.Species == species, cancellationToken).ConfigureAwait(false))?.Kills ?? 0;
                }

                if (!BestiaryRules.MayEquip(species, kills))
                {
                    return BestiaryStatus.NotEarned;
                }

                IRepository<EquippedBestiaryTitle> titles = unit.Repository<EquippedBestiaryTitle>();
                EquippedBestiaryTitle? equipped = await titles.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (species == MonsterKind.None)
                {
                    if (equipped is not null)
                    {
                        titles.Remove(equipped);
                    }
                }
                else if (equipped is null)
                {
                    titles.Add(new EquippedBestiaryTitle { PlayerId = playerId, Species = species });
                }
                else
                {
                    equipped.Species = species;
                    titles.Update(equipped, nameof(EquippedBestiaryTitle.Species));
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return BestiaryStatus.Ok;
            }
        }
    }
}
