// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Blacksmith;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Blacksmith;

/// <summary>The blacksmith's tool upgrades (docs/adr/ADR-026 D2). The server prices, times and moves everything; a handler only relays the intent.</summary>
[PacketHandler("Nosum.Blacksmith")]
public static partial class BlacksmithHandlers
{
    [Inject] private static IBlacksmithService s_smith = null!;

    /// <summary>Replies with the player's tool at the blacksmith.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ShopOpCode.GetBlacksmith)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "blacksmith-read")]
    public static async ValueTask HandleGetBlacksmithAsync(IPacketContext<GetBlacksmith> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BlacksmithView view = await s_smith.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, view).ConfigureAwait(false);
    }

    /// <summary>Hands a tool to the blacksmith for an upgrade.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ShopOpCode.UpgradeTool)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 4, PolicyId = "blacksmith-upgrade")]
    public static async ValueTask HandleUpgradeToolAsync(IPacketContext<UpgradeTool> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;
        BlacksmithView view = await s_smith.UpgradeAsync(context.Connection, itemId, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, view).ConfigureAwait(false);
    }

    /// <summary>Collects the upgraded tool.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ShopOpCode.CollectTool)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 4, PolicyId = "blacksmith-collect")]
    public static async ValueTask HandleCollectToolAsync(IPacketContext<CollectTool> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BlacksmithView view = await s_smith.CollectAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, view).ConfigureAwait(false);
    }

    private static ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, BlacksmithView view)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, BlacksmithState>(context, sequence, r =>
        {
            r.Status = view.Status;
            r.ToolItemId = view.ToolItemId;
            r.ReadyDay = view.ReadyDay;
            r.Today = view.Today;
        });
}
