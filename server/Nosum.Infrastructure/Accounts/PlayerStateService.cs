// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;
using Nosum.Infrastructure.Progress;

namespace Nosum.Infrastructure.Accounts;

/// <summary>
/// Stores the saved place of a player in their <see cref="PlayerState"/> row. The row is held in memory (<see cref="PlayerStateCache"/>, docs/adr/ADR-035 D4):
/// loading the place costs no command once the row was read, and a save writes only the place columns, after which the copy follows.
/// </summary>
public sealed class PlayerStateService : IPlayerStateService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly PlayerStateCache _cache;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="cache">The row copy shared with the progress service; <see langword="null"/> for a private one.</param>
    public PlayerStateService(IUnitOfWorkFactory unitOfWork, TimeProvider time, PlayerStateCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _time = time;
        _cache = cache ?? new PlayerStateCache();
    }

    /// <inheritdoc/>
    public async Task<PlayerStateRow?> LoadAsync(Guid playerId, CancellationToken cancellationToken)
    {
        PlayerStateCache.Snapshot state = await _cache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
        return state.Exists ? new PlayerStateRow(state.MapName, state.X, state.Y, state.Facing) : null;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Guid playerId, PlayerStateRow state, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        for (int attempt = 0; ; attempt++)
        {
            PlayerStateCache.Snapshot seen = await _cache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<PlayerState> states = unit.Repository<PlayerState>();
                if (seen.Exists)
                {
                    // Only the place columns: the progress service owns the others and may be saving them right now.
                    states.Update(
                        new PlayerState { PlayerId = playerId, MapName = state.MapName, X = state.X, Y = state.Y, Facing = state.Facing, UpdatedAt = now },
                        nameof(PlayerState.MapName),
                        nameof(PlayerState.X),
                        nameof(PlayerState.Y),
                        nameof(PlayerState.Facing),
                        nameof(PlayerState.UpdatedAt));
                }
                else
                {
                    states.Add(new PlayerState { PlayerId = playerId, MapName = state.MapName, X = state.X, Y = state.Y, Facing = state.Facing, EnergyRefilledDay = seen.EnergyRefilledDay, UpdatedAt = now });
                }

                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt == 0 && (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex)))
                {
                    // The row was made (or removed) behind the copy: read it again and write once more.
                    _cache.Forget(playerId);
                    continue;
                }
                catch
                {
                    _cache.Forget(playerId);
                    throw;
                }
            }

            _cache.Apply(playerId, s => s with { Exists = true, MapName = state.MapName, X = state.X, Y = state.Y, Facing = state.Facing, UpdatedAt = now });
            return;
        }
    }
}
