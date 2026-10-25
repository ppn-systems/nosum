// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Community;

/// <summary>
/// The permanent unlock flags set by completed community goals (docs/adr/ADR-030 D1). Other features ask it before they open content (a warp to
/// the Beach, a shop entry); the answer is server state and never depends on what a client says.
/// </summary>
public interface ICommunityUnlocks
{
    /// <summary>Tells whether a flag is set.</summary>
    /// <param name="key">The unlock key (<c>CommunityUnlockKeys</c>).</param>
    /// <returns><see langword="true"/> when a completed goal set the flag.</returns>
    bool IsUnlocked(string key);
}
