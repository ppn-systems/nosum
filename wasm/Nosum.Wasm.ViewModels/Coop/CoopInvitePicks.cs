// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Coops;

namespace Nosum.Wasm.ViewModels.Coop;

/// <summary>
/// The players the co-op invite picker offers: the players on the map, minus the members already in the co-op (critique-social finding 7,
/// docs/design/critique-social.md). A member is recognised by the label the picker shows (the same nickname format on both lists).
/// </summary>
public static class CoopInvitePicks
{
    /// <summary>Keeps the nearby players whose label is not a member's label.</summary>
    /// <param name="nearby">The players on the map, with the label each one shows.</param>
    /// <param name="memberLabels">The labels of the members already in the co-op (the founder included).</param>
    /// <returns>The players that can still be invited, in the order given.</returns>
    public static List<(uint Id, string Label)> Candidates(IEnumerable<(uint Id, string Label)> nearby, IEnumerable<string> memberLabels)
    {
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(memberLabels);

        HashSet<string> members = new(StringComparer.Ordinal);
        foreach (string label in memberLabels)
        {
            if (!string.IsNullOrEmpty(label))
            {
                _ = members.Add(label);
            }
        }

        List<(uint Id, string Label)> picks = [];
        foreach ((uint id, string label) in nearby)
        {
            if (!members.Contains(label))
            {
                picks.Add((id, label));
            }
        }

        return picks;
    }

    /// <summary>
    /// Whether an invitation the server accepted at <paramref name="sentAt"/> may still be open at <paramref name="now"/>. The server keeps an
    /// invitation for <see cref="CoopRules.InviteHours"/>; after that the picker offers the player again. A decline is not reported to the
    /// founder, so this window is the only end the client can see (critique-social round 6).
    /// </summary>
    /// <param name="sentAt">When the server accepted the invitation (the client's clock).</param>
    /// <param name="now">The current time (the client's clock).</param>
    /// <returns><see langword="true"/> while the invitation is inside its window.</returns>
    public static bool StillPending(DateTimeOffset sentAt, DateTimeOffset now) => now - sentAt < TimeSpan.FromHours(CoopRules.InviteHours);
}
