// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Pets;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Pet;
using Nosum.Domain.Pets;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Pets;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Quests;
using Nosum.Infrastructure.Shops;

namespace Nosum.Infrastructure.Pets;

/// <summary>
/// <see cref="IPetService"/> (docs/adr/ADR-075 D3-D5 and its paid-pet amendment). Adoption and petting are server decisions: one pet at a time, the
/// name cleaned and masked by <see cref="PetRules.CleanName"/>, one petting per game day (<see cref="GameCalendar.DayNumberAt"/>). A pet is bought
/// and rehomed at the carpenter's counter: the coins and the pet row move in one transfer, so a refused step changes neither. The energy bonus is
/// applied through <see cref="IPlayerProgress.RestoreEnergyAsync"/> after the petting's own change.
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
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly ITransferService _transfers;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="progress">Applies the petting's energy bonus.</param>
    /// <param name="calendar">The game calendar: the day a pet was last petted.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="presence">Where players stand: buying and rehoming need the carpenter's counter.</param>
    /// <param name="maps">The hosted maps (tile size) for the counter's reach.</param>
    /// <param name="transfers">Moves the coins and the pet row in one transaction.</param>
    /// <param name="quests">Hears about the player actions the tutorial chain counts; <see langword="null"/> to hear nothing.</param>
    public PetService(
        IUnitOfWorkFactory unitOfWork,
        IPlayerProgress progress,
        GameCalendar calendar,
        TimeProvider time,
        IMapPresence presence,
        IMapCatalog maps,
        ITransferService transfers,
        IQuestService? quests = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(transfers);

        _unitOfWork = unitOfWork;
        _progress = progress;
        _calendar = calendar;
        _time = time;
        _presence = presence;
        _maps = maps;
        _transfers = transfers;
        _quests = quests;
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
    public async Task<PetView> AdoptAsync(IConnection connection, Guid playerId, byte kind, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!PetRules.IsAdoptable(kind))
        {
            return None(PetStatus.InvalidKind);
        }

        string clean = PetRules.CleanName(name);
        if (clean.Length == 0)
        {
            return None(PetStatus.InvalidName);
        }

        if (!ShopCounterReach.IsAt(_presence, _maps, connection, ShopCatalog.CarpenterShop))
        {
            return None(PetStatus.NotAtShop);
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerPet row = new()
            {
                PlayerId = playerId,
                Kind = kind,
                Name = clean,
                Mood = PetRules.StartingMood,
                LastPettedDay = 0,
                AdoptedAt = _time.GetUtcNow(),
            };

            // The coins and the pet row are one transaction: a second pet, or too few coins, leaves both untouched.
            PetStatus refusal = PetStatus.Ok;
            TransferStatus status = await _transfers.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(playerId), 0, 0, -PetRules.PriceOf(kind))],
                "pet.adopt",
                async (unit, token) =>
                {
                    // One pet at a time, checked again inside the transaction: a second adoption is refused and rolled back.
                    if (await unit.Repository<PlayerPet>().FindAsync(playerId, token).ConfigureAwait(false) is not null)
                    {
                        refusal = PetStatus.AlreadyHasPet;
                        return TransferStatus.Refused;
                    }

                    unit.Repository<PlayerPet>().Add(row);
                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            if (status != TransferStatus.Ok)
            {
                return None(status switch
                {
                    TransferStatus.NotEnoughCoins => PetStatus.NotEnoughCoins,
                    TransferStatus.Refused => refusal,
                    _ => PetStatus.Unavailable,
                });
            }

            _looks[playerId] = new PetLook(kind, clean);
            await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.PetAdopted, kind), cancellationToken).ConfigureAwait(false);
            return View(PetStatus.Ok, row, this.Today());
        }
    }

    /// <inheritdoc/>
    public async Task<PetView> RehomeAsync(IConnection connection, Guid playerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!ShopCounterReach.IsAt(_presence, _maps, connection, ShopCatalog.CarpenterShop))
        {
            return None(PetStatus.NotAtShop);
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            long refund;
            await using (unit.ConfigureAwait(false))
            {
                PlayerPet? row = await unit.Repository<PlayerPet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (row is null)
                {
                    return None(PetStatus.NoPet);
                }

                refund = PetRules.RehomeRefundOf(row.Kind);
            }

            // The pet row is removed and the coins paid in one transaction; the row is read again inside it.
            PetStatus refusal = PetStatus.Ok;
            TransferStatus status = await _transfers.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(playerId), 0, 0, refund)],
                "pet.rehome",
                async (unit, token) =>
                {
                    PlayerPet? current = await unit.Repository<PlayerPet>().FindAsync(playerId, token).ConfigureAwait(false);
                    if (current is null)
                    {
                        refusal = PetStatus.NoPet;
                        return TransferStatus.Refused;
                    }

                    unit.Repository<PlayerPet>().Remove(current);
                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            if (status != TransferStatus.Ok)
            {
                return None(refusal == PetStatus.Ok ? PetStatus.Unavailable : refusal);
            }

            _looks[playerId] = PetLook.None;
            return None(PetStatus.Ok);
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
