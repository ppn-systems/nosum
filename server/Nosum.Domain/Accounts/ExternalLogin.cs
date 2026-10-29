// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>
/// A Google or Microsoft identity linked to a player account (docs/adr/ADR-069). The pair of provider and subject identifies the account;
/// an e-mail address never does.
/// </summary>
public sealed class ExternalLogin : Entity
{
    /// <summary>The longest subject accepted from a provider; a longer one is refused (the column holds this many characters).</summary>
    public const int MaxSubjectLength = 255;

    /// <summary>Gets or sets the player the identity signs in.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the provider that issued the identity.</summary>
    public ExternalProvider Provider { get; set; }

    /// <summary>Gets or sets the provider's stable subject (<c>sub</c>, or <c>oid</c> for Microsoft). Unique per provider.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets when the identity was linked.</summary>
    public DateTimeOffset LinkedAt { get; set; }
}
