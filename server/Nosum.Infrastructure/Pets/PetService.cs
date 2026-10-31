// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Pets;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Pet;
using Nosum.Domain.Pets;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Pets;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Quests;

namespace Nosum.Infrastructure.Pets;

/// <summary>
/// <see cref="IPetService"/> (docs/adr/ADR-075 D3-D5). Adoption and petting are server decisions: one pet at a time, the name cleaned and masked
/// by <see cref="PetRules.CleanName"/>, one petting per game day (<see cref="GameCalendar.DayNumberAt"/>). Each change is one unit of work under the
/// player's lock; the energy bonus is applied through <see cref="IPlayerProgress.RestoreEnergyAsync"/> after it, as its own change.
/// </summary>
public sealed class PetService : IPetService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IPlayerProgress _progress;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _locks = new();
    private readonly ConcurrentDictionary<Guid, PetLook> _looks = new();
    private readonly IQuestService? _quests;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="progress">Applies the petting's energy bonus.</param>
    /// <param name="calendar">The game calendar: the day a pet was last petted.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="quests">Hears about the player actions the tutorial chain counts; <see langword="null"/> to hear nothing.</param>
    public PetService(IUnitOfWorkFactory unitOfWork, IPlayerProgress progress, GameCalendar calendar, TimeProvider time, IQuestService? quests = null)
    {
        _quests = quests;
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _progress = progress;
        _calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<PetView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerPet? row = await unit.Repository<PlayerPet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return row is null ? None(PetStatus.Ok) : View(PetStatus.Ok, row, this.Today());
        }
    }

    /// <inheritdoc/>
    public async Task<PetView> AdoptAsync(Guid playerId, byte kind, string name, CancellationToken cancellationToken = default)
    {
        if (!PetRules.IsAdoptable(kind))
        {
            return None(PetStatus.InvalidKind);
        }

        string clean = PetRules.CleanName(name);
        if (clean.Length == 0)
        {
            return None(PetStatus.InvalidName);
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                PlayerPet? existing = await unit.Repository<PlayerPet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    return View(PetStatus.AlreadyHasPet, existing, this.Today());
                }

                PlayerPet row = new()
                {
                    PlayerId = playerId,
                    Kind = kind,
                    Name = clean,
                    Mood = PetRules.StartingMood,
                    LastPettedDay = 0,
                    AdoptedAt = _time.GetUtcNow(),
                };
                unit.Repository<PlayerPet>().Add(row);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _looks[playerId] = new PetLook(kind, clean);
                await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.PetAdopted, kind), cancellationToken).ConfigureAwait(false);
                return View(PetStatus.Ok, row, this.Today());
            }
        }
    }

    /// <inheritdoc/>
    public async Task<PetView> PetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        int today = this.Today();
        PetView view;
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                PlayerPet? row = await unit.Repository<PlayerPet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (row is null)
                {
                    return None(PetStatus.NoPet);
                }

                if (!PetRules.CanPet(row.LastPettedDay, today))
                {
                    return View(PetStatus.AlreadyPetted, row, today);
                }

                row.Mood = PetRules.Petted(row.Mood);
                row.LastPettedDay = today;
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                view = View(PetStatus.Ok, row, today);
            }
        }

        await _progress.RestoreEnergyAsync(playerId, PetRules.PettingEnergy, cancellationToken).ConfigureAwait(false);
        return view;
    }

    /// <inheritdoc/>
    public async ValueTask<PetLook> LookOfAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        if (_looks.TryGetValue(playerId, out PetLook cached))
        {
            return cached;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerPet? row = await unit.Repository<PlayerPet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            PetLook look = row is null ? PetLook.None : new PetLook(row.Kind, row.Name);
            _looks[playerId] = look;
            return look;
        }
    }

    private int Today() => _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());

    private static PetView None(PetStatus status) => new(status, (byte)PetKind.None, string.Empty, 0, false);

    private static PetView View(PetStatus status, PlayerPet row, int today)
        => new(status, row.Kind, row.Name, row.Mood, !PetRules.CanPet(row.LastPettedDay, today));
}
