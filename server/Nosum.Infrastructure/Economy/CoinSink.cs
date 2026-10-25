// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Abstractions.Economy;

namespace Nosum.Infrastructure.Economy;

/// <summary>The fee sink in use: fees are destroyed, nobody receives them (owner decision 2026-10-25, docs/adr/ADR-013 amendments).</summary>
public sealed class CoinSink : IFeeSink
{
    /// <inheritdoc/>
    public IReadOnlyList<TransferLeg> Collect(long fee, string reason) => [];
}
