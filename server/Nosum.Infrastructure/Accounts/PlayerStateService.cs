// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Accounts;

/// <summary>Stores one <see cref="PlayerState"/> row per player.</summary>
public sealed class PlayerStateService : IPlayerStateService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    public PlayerStateService(IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<PlayerStateRow?> LoadAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerState? state = await unit.Repository<PlayerState>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return state is null ? null : new PlayerStateRow(state.MapName, state.X, state.Y, state.Facing);
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Guid playerId, PlayerStateRow state, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<PlayerState> states = unit.Repository<PlayerState>();
            PlayerState? row = await states.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new PlayerState { PlayerId = playerId };
                states.Add(row);
            }

            row.MapName = state.MapName;
            row.X = state.X;
            row.Y = state.Y;
            row.Facing = state.Facing;
            row.UpdatedAt = _time.GetUtcNow();
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
