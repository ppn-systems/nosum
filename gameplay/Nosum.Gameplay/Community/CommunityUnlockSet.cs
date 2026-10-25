// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Community;

/// <summary>
/// The community unlocks the client has to know to predict its own walking (docs/adr/ADR-037 D4), one bit each. Only unlocks that change a map's walk grid are here;
/// the stall and lantern unlocks (<see cref="CommunityUnlockKeys"/>) stay on the server.
/// </summary>
[Flags]
public enum CommunityUnlockSet : byte
{
    /// <summary>Nothing is unlocked.</summary>
    None = 0,

    /// <summary>The bridge to the east part of the Beach is built (<see cref="CommunityUnlockKeys.BeachBridge"/>).</summary>
    BeachBridge = 1 << 0,
}

/// <summary>Packing of <see cref="CommunityUnlockSet"/> for the wire.</summary>
public static class CommunityUnlockWire
{
    /// <summary>Packs the set the client needs.</summary>
    /// <param name="isUnlocked">Whether a key is unlocked (the server's <c>ICommunityUnlocks.IsUnlocked</c>).</param>
    /// <returns>The flags as sent in the packets.</returns>
    public static byte Pack(Func<string, bool> isUnlocked)
    {
        ArgumentNullException.ThrowIfNull(isUnlocked);

        CommunityUnlockSet flags = CommunityUnlockSet.None;
        if (isUnlocked(CommunityUnlockKeys.BeachBridge))
        {
            flags |= CommunityUnlockSet.BeachBridge;
        }

        return (byte)flags;
    }

    /// <summary>Tells whether a key is set in packed flags.</summary>
    /// <param name="flags">The flags as received.</param>
    /// <param name="key">An unlock key (<see cref="CommunityUnlockKeys"/>).</param>
    /// <returns><see langword="true"/> when the key is one the wire carries and its bit is set.</returns>
    public static bool IsSet(byte flags, string key)
        => string.Equals(key, CommunityUnlockKeys.BeachBridge, StringComparison.Ordinal) && ((CommunityUnlockSet)flags & CommunityUnlockSet.BeachBridge) != 0;
}
