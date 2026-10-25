// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Coops;

/// <summary><see cref="ICoopMembership"/> on PostgreSQL: two indexed lookups per call (membership, then the co-op row for its land level), never cached, so a leave or a kick is felt at once.</summary>
public sealed class CoopMembership : ICoopMembership
{
    private readonly IUnitOfWorkFactory _unitOfWork;

    /// <summary>Initializes the lookup.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    public CoopMembership(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async Task<CoopMembershipInfo?> FindAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            CoopMember? member = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            if (member is null)
            {
                return null;
            }

            Coop? coop = await unit.Repository<Coop>().FindAsync(member.CoopId, cancellationToken).ConfigureAwait(false);
            return new CoopMembershipInfo(member.CoopId, member.Id, member.Role, coop?.LandLevel ?? 0);
        }
    }
}
