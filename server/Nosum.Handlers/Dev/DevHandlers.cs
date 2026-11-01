// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Fishing;
using Nosum.Abstractions.Friends;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Dev;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Dev;

/// <summary>
/// Tester tools. The host maps this class ONLY when it runs in the Development environment (<c>Program.BuildApplication</c>); in Production the
/// opcode is not registered, so a frame for it is dropped and <see cref="IDevGrantService"/> does not exist.
/// </summary>
[PacketHandler("Nosum.Dev")]
public static partial class DevHandlers
{
    [Inject] private static IDevGrantService s_grants = null!;
    [Inject] private static IDevClockService s_clock = null!;
    [Inject] private static IPlayerService s_players = null!;
    [Inject] private static IFishingService s_fishing = null!;
    [Inject] private static IPlayerNotifier s_notifier = null!;
    [Inject] private static IDevSocialService s_social = null!;

    /// <summary>Gives the sender items and/or coins.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevGrant)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-grant")]
    public static async ValueTask HandleDevGrantAsync(IPacketContext<DevGrant> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        long coins = context.Packet.Coins;
        PlayerIdentity player = context.Connection.GetPlayer();

        bool ok = await s_grants.GrantAsync(player.PlayerId, itemId, quantity, coins, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevGrant, DevGrantResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Moves the server's game clock forward by whole game days (crop growth and day rollovers can then be tested at once).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevAdvanceClock)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-clock")]
    public static async ValueTask HandleDevAdvanceClockAsync(IPacketContext<DevAdvanceClock> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        long? now = s_clock.AdvanceDays(context.Packet.Days);
        await Replies.ReplyAsync<DevAdvanceClock, DevAdvanceClockResult>(context, sequence, r =>
        {
            r.Ok = now is not null;
            r.ServerNowMs = now ?? 0;
        }).ConfigureAwait(false);
    }

    /// <summary>Moves the sender to a tile of a map (docs/adr/ADR-079), through the same steps as a travel.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevTeleport)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-teleport")]
    public static async ValueTask HandleDevTeleportAsync(IPacketContext<DevTeleport> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool ok = await s_players.TeleportAsync(context.Connection, context.Packet.MapId, context.Packet.TileX, context.Packet.TileY, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevTeleport, DevTeleportResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Moves the server clock to the start of the next season, as many times as asked (docs/adr/ADR-079).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevSeason)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-season")]
    public static async ValueTask HandleDevSeasonAsync(IPacketContext<DevSeason> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool ok = s_clock.AdvanceSeasons(context.Packet.Seasons) is not null;
        await Replies.ReplyAsync<DevSeason, DevSeasonResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Pushes a level-up banner to the sender; the skill itself does not change (docs/adr/ADR-079).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevLevelUp)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-level-up")]
    public static async ValueTask HandleDevLevelUpAsync(IPacketContext<DevLevelUp> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();
        using PacketScope<LevelUp> banner = PacketFactory<LevelUp>.Acquire();
        banner.Value.Skill = (byte)context.Packet.Skill;
        banner.Value.Level = (byte)context.Packet.Level;
        bool ok = await s_notifier.TrySendAsync(player.PlayerId, banner.Value, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevLevelUp, DevLevelUpResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Makes the sender's open fishing cast bite at once (docs/adr/ADR-079).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevFishingBite)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-fishing-bite")]
    public static async ValueTask HandleDevFishingBiteAsync(IPacketContext<DevFishingBite> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool ok = await s_fishing.MakeBiteAsync(context.Connection, context.Packet.CastId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevFishingBite, DevFishingBiteResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Makes the sender a registered test account and befriends a new registered test player, whose nickname and session token come back
    /// (docs/adr/ADR-100).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevBefriend)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-befriend")]
    public static async ValueTask HandleDevBefriendAsync(IPacketContext<DevBefriend> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();
        DevFriend? made = await s_social.BefriendNewTestPlayerAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevBefriend, DevBefriendResult>(context, sequence, r =>
        {
            r.Ok = made is not null;
            r.Nickname = made?.Nickname ?? string.Empty;
            r.SessionToken = made?.SessionToken ?? string.Empty;
        }).ConfigureAwait(false);
    }
}
