// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Economy;

/// <summary>A player's coin balance (one row per player). Changed only by the transfer service, in the same transaction as the items it pays for.</summary>
public sealed class PlayerWallet
{
    /// <summary>Gets or sets the owning player; also this row's primary key.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the balance. Also a concurrency token: two writers of one balance cannot both win.</summary>
    public long Coins { get; set; }

    /// <summary>Gets or sets when the wallet was created (and given the starting coins).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
