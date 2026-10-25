// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Inventory;

/// <summary>
/// Inventory owners that are not players (docs/adr/ADR-024 D4). An inventory is stored per owner id; a player's is the account id, a chest's is
/// derived from the id of the placed object, so both live in the same tables and go through the same atomic changes. The derived id carries a
/// fixed marker, so it is recognised without a lookup and cannot be an account id (those are random).
/// </summary>
public static class ContainerIds
{
    private const short MarkerA = 0x4E53;
    private const short MarkerB = 0x4343;
    private static readonly byte[] s_tail = [0x43, 0x48, 0x45, 0x53, 0x54, 0x00, 0x00, 0x01];

    /// <summary>Returns the inventory owner id of a chest.</summary>
    /// <param name="objectId">The placed object's id.</param>
    /// <returns>The owner id.</returns>
    public static Guid ForChest(int objectId) => new(objectId, MarkerA, MarkerB, s_tail);

    /// <summary>Tells whether an inventory owner id is a container's rather than a player's.</summary>
    /// <param name="ownerId">An inventory owner id.</param>
    /// <returns><see langword="true"/> for ids made by <see cref="ForChest"/>.</returns>
    public static bool IsContainer(Guid ownerId) => TryGetChest(ownerId, out _);

    /// <summary>Reads the placed object id out of a chest's owner id.</summary>
    /// <param name="ownerId">An inventory owner id.</param>
    /// <param name="objectId">The placed object id when it is a chest's.</param>
    /// <returns><see langword="true"/> for ids made by <see cref="ForChest"/>.</returns>
    public static bool TryGetChest(Guid ownerId, out int objectId)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = ownerId.TryWriteBytes(bytes);
        bool marked = bytes[4] == (byte)(MarkerA & 0xFF) && bytes[5] == (byte)((MarkerA >> 8) & 0xFF)
            && bytes[6] == (byte)(MarkerB & 0xFF) && bytes[7] == (byte)((MarkerB >> 8) & 0xFF)
            && bytes[8..].SequenceEqual(s_tail);
        objectId = marked ? BitConverter.ToInt32(bytes[..4]) : 0;
        return marked;
    }
}
