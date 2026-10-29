// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Journal;
using Nosum.Domain.Journal;

namespace Nosum.Infrastructure.Journal;

/// <summary>
/// <see cref="IJournalService"/> on PostgreSQL: reads the player's <see cref="PlayerDiscovery"/> rows. A read takes one unit of work and no lock; the
/// rows are written only by <see cref="JournalRecorder"/>, inside the inventory transaction that granted the items.
/// </summary>
public sealed class JournalService : IJournalService
{
    private readonly IUnitOfWorkFactory _unitOfWork;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    public JournalService(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JournalLine>> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<PlayerDiscovery> rows = await unit.Repository<PlayerDiscovery>().ListAsync(d => d.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            return [.. rows.OrderBy(d => d.ItemId).Select(d => new JournalLine(d.ItemId, d.Count, d.FirstObtainedAt))];
        }
    }
}
