// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Accounts;

/// <summary>
/// The hook features call to invite a guest to register (for example when the first quest is finished).
/// The flag is stored on the player and reaches the client in the account result.
/// </summary>
public interface IRegistrationPromptService
{
    /// <summary>Unlocks the registration invitation for a guest. Does nothing for a registered player or when already unlocked.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when this call unlocked it (the caller may now tell the client); false otherwise.</returns>
    Task<bool> UnlockAsync(Guid playerId, CancellationToken cancellationToken);
}
