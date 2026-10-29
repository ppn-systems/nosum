// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Security;

/// <summary>
/// Counts events per key inside a sliding window and refuses the ones past a limit (docs/adr/ADR-032 D6): new accounts
/// per address per day. In memory, per process.
/// </summary>
public interface IAbuseGuard
{
    /// <summary>Records one event for a key unless the key is already at its limit.</summary>
    /// <param name="bucket">What is counted, for example <c>new-account</c>.</param>
    /// <param name="key">Who is counted, for example an address.</param>
    /// <param name="limit">Most events allowed inside <paramref name="window"/>.</param>
    /// <param name="window">The sliding window.</param>
    /// <returns><see langword="true"/> when the event is allowed and counted; <see langword="false"/> when the limit is reached (nothing is counted).</returns>
    bool TryAcquire(string bucket, string key, int limit, TimeSpan window);
}
