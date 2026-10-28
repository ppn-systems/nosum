// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.GroundItems;

/// <summary>
/// Rules for items lying on the ground (docs/adr/ADR-021 D5-D6): how close a player must be to take one, who may take it
/// when, when it vanishes, and the 16 x 16 tile chunk it is filed under (docs/adr/ADR-022 D1). Pure: the clock is a parameter.
/// </summary>
public static class GroundItemRules
{
    /// <summary>How far a player's feet may be from an item to pick it up: 1.5 tiles of 16 pixels.</summary>
    public const double PickupRadiusPixels = 24;

    /// <summary>How long the player who caused a drop has it to themselves on a shared map, milliseconds.</summary>
    public const long OwnerPriorityMs = 5_000;

    /// <summary>How long an item lies on a shared map before it vanishes, milliseconds (10 minutes).</summary>
    public const long SharedExpiryMs = 600_000;

    /// <summary>Side of a chunk, tiles (docs/adr/ADR-022 D1).</summary>
    public const int ChunkTiles = 16;

    /// <summary>The farthest from the pop spot an item is nudged toward the player so it lies inside the pickup radius, pixels.</summary>
    private const double PopMargin = 4;

    /// <summary>Tells whether a player's feet are close enough to take an item.</summary>
    /// <param name="itemX">Item x, pixels.</param>
    /// <param name="itemY">Item y, pixels.</param>
    /// <param name="feetX">The player's authoritative x, pixels.</param>
    /// <param name="feetY">The player's authoritative y, pixels.</param>
    /// <returns><see langword="true"/> when the item is within <see cref="PickupRadiusPixels"/>.</returns>
    public static bool InPickupRange(double itemX, double itemY, double feetX, double feetY)
    {
        double dx = itemX - feetX;
        double dy = itemY - feetY;
        return (dx * dx) + (dy * dy) <= PickupRadiusPixels * PickupRadiusPixels;
    }

    /// <summary>Gets the chunk coordinate of a pixel coordinate.</summary>
    /// <param name="pixel">Pixel x or y.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns>The chunk index along that axis (never negative).</returns>
    public static int ChunkOf(double pixel, int tileSize)
    {
        int tile = (int)Math.Floor(pixel / tileSize);
        return Math.Max(0, tile) / ChunkTiles;
    }

    /// <summary>Gives the timing of a new drop.</summary>
    /// <param name="isPrivateInstance">Whether the drop lies in a player's private instance: no priority window, never expires.</param>
    /// <param name="nowMs">Now, Unix milliseconds.</param>
    /// <param name="priorityUntilMs">Until when the cause has priority; zero when none.</param>
    /// <param name="expiresAtMs">When the item vanishes; zero when it never does.</param>
    public static void Timing(bool isPrivateInstance, long nowMs, out long priorityUntilMs, out long expiresAtMs)
    {
        priorityUntilMs = isPrivateInstance ? 0 : nowMs + OwnerPriorityMs;
        expiresAtMs = isPrivateInstance ? 0 : nowMs + SharedExpiryMs;
    }

    /// <summary>Tells whether a player may take an item now.</summary>
    /// <param name="isPrivateInstance">Whether the item lies in a player's private instance.</param>
    /// <param name="instanceOwner">The owner of the private instance, or <see cref="Guid.Empty"/>.</param>
    /// <param name="causedBy">The player who caused the drop, or <see langword="null"/>.</param>
    /// <param name="priorityUntilMs">Until when the cause has priority (shared maps).</param>
    /// <param name="playerId">The player who wants it.</param>
    /// <param name="nowMs">Now, Unix milliseconds.</param>
    /// <param name="isAuthorizedOnInstance">Whether the player is an authorized owner or co-op member of the instance (for example inside their own or their co-op's building interior).</param>
    /// <returns><see langword="true"/> when the player may take it.</returns>
    public static bool CanPickUp(bool isPrivateInstance, Guid instanceOwner, Guid? causedBy, long priorityUntilMs, Guid playerId, long nowMs, bool isAuthorizedOnInstance = false)
    {
        if (isPrivateInstance)
        {
            // Nobody walks off with a farm's harvest: only the owner, an authorized member of the instance, or whoever caused the drop may take it.
            return playerId == instanceOwner || isAuthorizedOnInstance || (causedBy is { } cause && cause == playerId);
        }

        return isAuthorizedOnInstance || causedBy is not { } owner || owner == playerId || nowMs >= priorityUntilMs;
    }

    /// <summary>Tells whether an item has lain too long.</summary>
    /// <param name="expiresAtMs">When it vanishes; zero when never.</param>
    /// <param name="nowMs">Now, Unix milliseconds.</param>
    /// <returns><see langword="true"/> when it is gone.</returns>
    public static bool IsExpired(long expiresAtMs, long nowMs) => expiresAtMs > 0 && nowMs >= expiresAtMs;

    /// <summary>
    /// Finds where a popped item lies: the centre of the source tile, nudged toward the player when needed so the player
    /// can pick it up at once (a tile worked from the edge of the 32 px reach is farther than the pickup radius).
    /// </summary>
    /// <param name="tileCentreX">Centre of the source tile, pixels.</param>
    /// <param name="tileCentreY">Centre of the source tile, pixels.</param>
    /// <param name="feetX">The player's x, pixels.</param>
    /// <param name="feetY">The player's y, pixels.</param>
    /// <param name="x">The item's x, pixels.</param>
    /// <param name="y">The item's y, pixels.</param>
    public static void PopPosition(double tileCentreX, double tileCentreY, double feetX, double feetY, out double x, out double y)
    {
        double dx = feetX - tileCentreX;
        double dy = feetY - tileCentreY;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double limit = PickupRadiusPixels - PopMargin;
        if (distance <= limit)
        {
            x = tileCentreX;
            y = tileCentreY;
            return;
        }

        double move = distance - limit;
        x = tileCentreX + (dx / distance * move);
        y = tileCentreY + (dy / distance * move);
    }
}
