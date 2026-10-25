// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Accounts;

/// <summary>Small player flags other features set: activity and the registration invitation.</summary>
public sealed class PlayerLifecycleService : IPlayerActivityService, IRegistrationPromptService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    public PlayerLifecycleService(IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task TouchAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is not null)
            {
                player.LastActiveAt = _time.GetUtcNow();
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UnlockAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (player is null || !player.IsGuest || player.RegistrationPromptUnlockedAt is not null)
            {
                return false;
            }

            player.RegistrationPromptUnlockedAt = _time.GetUtcNow();
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
