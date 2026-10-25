// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Placeables;

/// <summary>Limits on placed objects, shared by the server and the client.</summary>
public static class PlacementLimits
{
    /// <summary>Most pieces one instance may hold, so a private room cannot grow without bound.</summary>
    public const int MaxPerInstance = 200;
}
