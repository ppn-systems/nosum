// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Visits;

/// <summary>
/// The farm guestbook (docs/adr/ADR-066): a log, kept for the owner, of the crops a visitor waters on their farm. It is written only while a
/// guestbook stands on the farm, and an owner reads at most <see cref="MaxRows"/> of its newest days at a time.
/// </summary>
public static class GuestbookRules
{
    /// <summary>Most rows one read of the guestbook returns, newest day first.</summary>
    public const int MaxRows = 30;

    /// <summary>
    /// Tells whether a watering is written to the guestbook: it was done by a visitor on somebody else's private farm, and a guestbook stands on that farm.
    /// The owner's own watering is never logged.
    /// </summary>
    /// <param name="instance">The place the crop is in.</param>
    /// <param name="playerId">Who watered it.</param>
    /// <param name="guestbookStands">Whether a guestbook placeable stands in the place.</param>
    /// <returns><see langword="true"/> when the watering is logged.</returns>
    public static bool Logs(MapInstance instance, Guid playerId, bool guestbookStands) => guestbookStands && VisitRules.IsVisitor(instance, playerId);
}
