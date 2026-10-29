// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Combat;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Combat;

/// <summary>
/// The hunting log and its slayer titles (docs/adr/ADR-067). The identity is the connection's, so a player reads and equips only their own log.
/// Kills are counted by the server's defeat path, never by a request from the client.
/// </summary>
[PacketHandler("Nosum.Bestiary")]
public static partial class BestiaryHandlers
{
    [Inject] private static IBestiaryService s_bestiary = null!;

    /// <summary>Replies with the player's kill counts and the equipped title.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.GetBestiary)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "bestiary-read")]
    public static async ValueTask HandleGetBestiaryAsync(IPacketContext<GetBestiary> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        BestiaryView view = await s_bestiary.ReadAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetBestiary, BestiarySnapshot>(context, sequence, view.FillSnapshot).ConfigureAwait(false);
    }

    /// <summary>Equips a slayer title the player has earned, or takes the title off.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.EquipBestiaryTitle)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "bestiary-write")]
    public static async ValueTask HandleEquipBestiaryTitleAsync(IPacketContext<EquipBestiaryTitle> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        MonsterKind species = (MonsterKind)context.Packet.Species;
        PlayerIdentity player = context.Connection.GetPlayer();

        BestiaryStatus status = await s_bestiary.EquipTitleAsync(player.PlayerId, species, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<EquipBestiaryTitle, EquipBestiaryTitleResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
