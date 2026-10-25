// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Placeables;

/// <summary>Limits on placed objects, shared by the server and the client.</summary>
public static class PlacementLimits
{
    /// <summary>Most pieces one instance may hold, so a private place cannot grow without bound. A fenced field takes dozens of pieces, so the farm allows more than a room needs (docs/adr/ADR-024).</summary>
    public const int MaxPerInstance = 400;
}
