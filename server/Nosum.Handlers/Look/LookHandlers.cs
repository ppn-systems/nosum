// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Look;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Look;

/// <summary>Player looks and the wardrobe. The server decides and stores every look and sends ids only (docs/adr/ADR-006 D6, ADR-014).</summary>
[PacketHandler("Nosum.Look")]
public static partial class LookHandlers
{
    [Inject] private static ILookService s_looks = null!;

    /// <summary>Replies with the looks of everyone on the map and announces the player's own look to the others.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(LookOpCode.GetLooks)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "look-read")]
    public static async ValueTask HandleGetLooksAsync(IPacketContext<GetLooks> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        IReadOnlyList<PlayerLookView> looks = await s_looks.AnnounceAndGetAsync(context.Connection, player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetLooks, LooksSnapshot>(context, sequence, r =>
        {
            r.Looks.Clear();
            foreach (PlayerLookView view in looks)
            {
                LookRow row = LookRow.Create();
                view.FillRow(row);
                r.Looks.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Chooses skin, hair style and hair dye.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(LookOpCode.SetAppearance)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "look-write")]
    public static async ValueTask HandleSetAppearanceAsync(IPacketContext<SetAppearance> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int skin = context.Packet.SkinId;
        int style = context.Packet.HairStyleId;
        int dye = context.Packet.HairDyeId;
        PlayerIdentity player = context.Connection.GetPlayer();

        LookStatus status = await s_looks.SetAppearanceAsync(context.Connection, player.PlayerId, skin, style, dye, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<SetAppearance, LookResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Wears a clothing item from an inventory slot.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(LookOpCode.EquipItem)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(6, 3, PolicyId = "look-write")]
    public static async ValueTask HandleEquipItemAsync(IPacketContext<EquipItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        int itemId = context.Packet.ItemId;
        PlayerIdentity player = context.Connection.GetPlayer();

        LookStatus status = await s_looks.EquipAsync(context.Connection, player.PlayerId, slot, itemId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<EquipItem, LookResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Takes a worn item off.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(LookOpCode.UnequipItem)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(6, 3, PolicyId = "look-write")]
    public static async ValueTask HandleUnequipItemAsync(IPacketContext<UnequipItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int wearSlot = context.Packet.WearSlot;
        PlayerIdentity player = context.Connection.GetPlayer();

        LookStatus status = await s_looks.UnequipAsync(context.Connection, player.PlayerId, wearSlot, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<UnequipItem, LookResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
