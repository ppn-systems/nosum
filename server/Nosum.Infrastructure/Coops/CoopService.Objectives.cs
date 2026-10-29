// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Domain.Coops;
using Nosum.Gameplay.Coops;

namespace Nosum.Infrastructure.Coops;

/// <summary>The shared objectives of co-ops (docs/adr/ADR-067): harvests of a member add to the co-op's ledger, and the co-op keeps the trophy.</summary>
public sealed partial class CoopService
{
    /// <inheritdoc/>
    public async Task ContributeAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0 || !CoopObjectiveCatalog.TryForItem(itemId, out CoopObjectiveDefinition? objective))
        {
            return;
        }

        // The gate serializes every change of the co-op's rows, so the ledger is read, added to and written without a lost update.
        List<Guid> toPush;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Coop? coop = await unit.Repository<Coop>().FindAsync(me.CoopId, cancellationToken).ConfigureAwait(false);
                if (coop is null)
                {
                    return;
                }

                // The ledger is replaced, not edited in place, so the change is seen by the save.
                CoopObjectiveProgress before = coop.Objectives.GetValueOrDefault(objective.Id, new CoopObjectiveProgress(0, null));
                int after = CoopObjectiveRules.Credit(before.Contributed, quantity);
                bool earns = CoopObjectiveRules.EarnsTrophy(before.Contributed, after, objective.Target, before.CompletedAt is not null);
                Dictionary<int, CoopObjectiveProgress> ledger = new(coop.Objectives)
                {
                    [objective.Id] = new CoopObjectiveProgress(after, earns ? _time.GetUtcNow() : before.CompletedAt),
                };
                coop.Objectives = ledger;
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _directory.Changed(me.CoopId);
            toPush = await this.MemberPlayersAsync(playerId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }

        await this.PushToAsync(toPush, cancellationToken).ConfigureAwait(false);
    }
}
