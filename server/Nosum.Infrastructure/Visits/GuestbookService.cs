// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Visits;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Visits;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Visits;

/// <summary>
/// <see cref="IGuestbookService"/>: one <see cref="GuestbookEntry"/> row per owner, visitor and day in PostgreSQL. A count is a read-modify-write, so
/// the writes and reads of one owner's guestbook run one at a time under a per-owner lock: two first waterings on the same day add up instead of one
/// overwriting the other. The primary key stays the backstop (docs/adr/ADR-066).
/// </summary>
public sealed class GuestbookService : IGuestbookService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly INicknameDirectory _nicknames;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _ownerLocks = new(16);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="nicknames">Resolves visitors' nicknames for a read.</param>
    /// <param name="time">The clock for the update timestamps; tests pass a fake one.</param>
    public GuestbookService(IUnitOfWorkFactory unitOfWork, INicknameDirectory nicknames, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _nicknames = nicknames;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task RecordWateringAsync(Guid ownerId, Guid visitorId, int dayNumber, CancellationToken cancellationToken = default)
    {
        using (await _ownerLocks.EnterAsync(ownerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<GuestbookEntry> entries = unit.Repository<GuestbookEntry>();
                GuestbookEntry? entry = await entries.FirstOrDefaultAsync(e => e.OwnerId == ownerId && e.VisitorId == visitorId && e.DayNumber == dayNumber, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                {
                    entries.Add(new GuestbookEntry { OwnerId = ownerId, VisitorId = visitorId, DayNumber = dayNumber, Waters = 1, Unread = true, UpdatedAt = _time.GetUtcNow() });
                }
                else
                {
                    entry.Waters++;
                    entry.Unread = true;
                    entry.UpdatedAt = _time.GetUtcNow();
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<GuestbookView> ReadAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        using (await _ownerLocks.EnterAsync(ownerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<GuestbookEntry> entries = unit.Repository<GuestbookEntry>();
                List<GuestbookEntry> unread = await entries.ListAsync(e => e.OwnerId == ownerId && e.Unread, cancellationToken).ConfigureAwait(false);
                List<GuestbookEntry> newest = await entries.PageAsync(e => e.OwnerId == ownerId, e => e.DayNumber, 0, GuestbookRules.MaxRows, cancellationToken).ConfigureAwait(false);

                // Read the flags before clearing them: a row can be in both lists, and the tracker hands back the same instance.
                HashSet<(Guid, int)> newMarks = [.. unread.Select(e => (e.VisitorId, e.DayNumber))];
                int unreadCount = unread.Count;
                foreach (GuestbookEntry entry in unread)
                {
                    entry.Unread = false;
                    entries.Update(entry, nameof(GuestbookEntry.Unread));
                }

                IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(newest.Select(e => e.VisitorId).Distinct(), cancellationToken).ConfigureAwait(false);
                List<GuestbookLine> rows = new(newest.Count);
                foreach (GuestbookEntry entry in newest)
                {
                    string nickname = names.TryGetValue(entry.VisitorId, out string? name) ? name : string.Empty;
                    rows.Add(new GuestbookLine(entry.VisitorId, nickname, entry.DayNumber, entry.Waters, newMarks.Contains((entry.VisitorId, entry.DayNumber))));
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new GuestbookView(unreadCount, rows);
            }
        }
    }
}
