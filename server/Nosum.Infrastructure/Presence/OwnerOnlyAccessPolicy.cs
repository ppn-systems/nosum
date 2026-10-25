// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Presence;

/// <summary>A shared map is open to everybody; a private instance only to its owner (docs/adr/ADR-015 D4). Visiting later swaps this for a policy with an allow-list.</summary>
public sealed class OwnerOnlyAccessPolicy : IInstanceAccessPolicy
{
    /// <inheritdoc/>
    public bool CanEnter(MapInstance instance, Guid playerId) => !instance.IsPrivate || instance.OwnerId == playerId;
}
