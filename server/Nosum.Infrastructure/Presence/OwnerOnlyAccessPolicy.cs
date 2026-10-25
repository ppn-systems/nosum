// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Presence;

/// <summary>A shared map is open to everybody; a private instance only to its owner (docs/adr/ADR-015 D4). The host uses <c>VisitAccess</c>, which also admits visitors (docs/adr/ADR-019); this policy stays for tests that want no visiting.</summary>
public sealed class OwnerOnlyAccessPolicy : IInstanceAccessPolicy
{
    /// <inheritdoc/>
    public ValueTask<bool> CanEnterAsync(MapInstance instance, Guid playerId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(!instance.IsPrivate || instance.OwnerId == playerId);
}
