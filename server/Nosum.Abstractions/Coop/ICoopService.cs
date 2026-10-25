// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;

namespace Nosum.Abstractions.Coop;

/// <summary>A member of a co-op.</summary>
/// <param name="MemberId">The membership id.</param>
/// <param name="Role">The role.</param>
/// <param name="IsMe">Whether this is the viewer.</param>
/// <param name="Online">Whether the member is connected.</param>
/// <param name="Nickname">The member's nickname.</param>
public sealed record CoopMemberView(Guid MemberId, CoopRole Role, bool IsMe, bool Online, string Nickname);

/// <summary>One item kind in the warehouse.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many the warehouse holds.</param>
/// <param name="Available">How many the viewer may take out.</param>
public sealed record CoopStockView(int ItemId, int Quantity, int Available);

/// <summary>One warehouse log line.</summary>
/// <param name="MemberId">Who (empty when they left).</param>
/// <param name="ItemId">The item.</param>
/// <param name="Delta">Positive for a deposit, negative for a withdrawal.</param>
/// <param name="At">When.</param>
public sealed record CoopLogView(Guid MemberId, int ItemId, int Delta, DateTimeOffset At);

/// <summary>An invitation to the viewer.</summary>
/// <param name="InviteId">The invitation id.</param>
/// <param name="CoopName">The co-op's authored name.</param>
/// <param name="InviterNickname">The nickname of the co-op's founder, who invites; empty when unknown.</param>
public sealed record CoopInviteView(Guid InviteId, string CoopName, string InviterNickname);

/// <summary>A viewer's co-op as the service reports it.</summary>
/// <param name="HasCoop">Whether the viewer belongs to a co-op.</param>
/// <param name="Name">The authored name.</param>
/// <param name="Role">The viewer's role.</param>
/// <param name="Members">The members.</param>
/// <param name="Stock">The warehouse.</param>
/// <param name="Log">The latest log lines, newest first.</param>
/// <param name="Invites">Open invitations to the viewer.</param>
/// <param name="ServerNow">The server clock when read.</param>
public sealed record CoopView(
    bool HasCoop,
    string Name,
    CoopRole Role,
    IReadOnlyList<CoopMemberView> Members,
    IReadOnlyList<CoopStockView> Stock,
    IReadOnlyList<CoopLogView> Log,
    IReadOnlyList<CoopInviteView> Invites,
    DateTimeOffset ServerNow)
{
    /// <summary>Copies the view into a snapshot packet (the packet's lists are cleared first).</summary>
    /// <param name="packet">The packet to fill.</param>
    public void FillSnapshot(CoopSnapshot packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        packet.HasCoop = this.HasCoop;
        packet.Name = this.Name;
        packet.Role = this.Role;
        packet.ServerNowMs = this.ServerNow.ToUnixTimeMilliseconds();
        packet.Members.Clear();
        packet.Stock.Clear();
        packet.Log.Clear();
        packet.Invites.Clear();
        foreach (CoopMemberView member in this.Members)
        {
            CoopMemberRow row = CoopMemberRow.Create();
            row.MemberId = member.MemberId;
            row.Role = member.Role;
            row.IsMe = member.IsMe;
            row.Online = member.Online;
            row.Nickname = member.Nickname;
            packet.Members.Add(row);
        }

        foreach (CoopStockView stock in this.Stock)
        {
            CoopStockRow row = CoopStockRow.Create();
            row.ItemId = stock.ItemId;
            row.Quantity = stock.Quantity;
            row.Available = stock.Available;
            packet.Stock.Add(row);
        }

        foreach (CoopLogView line in this.Log)
        {
            CoopLogRow row = CoopLogRow.Create();
            row.MemberId = line.MemberId;
            row.ItemId = line.ItemId;
            row.Delta = line.Delta;
            row.AtMs = line.At.ToUnixTimeMilliseconds();
            packet.Log.Add(row);
        }

        foreach (CoopInviteView invite in this.Invites)
        {
            CoopInviteRow row = CoopInviteRow.Create();
            row.InviteId = invite.InviteId;
            row.CoopName = invite.CoopName;
            row.InviterNickname = invite.InviterNickname;
            packet.Invites.Add(row);
        }
    }
}

/// <summary>
/// Co-ops (docs/adr/ADR-014): found, invite, accept, leave, kick, and the shared warehouse with deposits, withdrawals and an audit
/// trail. One co-op per player. The founder takes anything from the warehouse; a member takes back what they put in. Changes are
/// pushed to every online member as a snapshot.
/// </summary>
public interface ICoopService
{
    /// <summary>
    /// Raised with the player's account id after a player stopped being a member of a co-op (they left, or the founder removed them).
    /// Visiting listens to it: co-op membership can grant entry to a farm, and ending it takes the grant away (docs/adr/ADR-019 D5).
    /// </summary>
    event Func<Guid, ValueTask>? MembershipEnded;

    /// <summary>Reads the viewer's co-op (or, without one, their open invitations).</summary>
    /// <param name="playerId">The viewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The view.</returns>
    Task<CoopView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Founds a co-op with the player as founder.</summary>
    /// <param name="playerId">The founder.</param>
    /// <param name="name">The name as typed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> CreateAsync(Guid playerId, string name, CancellationToken cancellationToken = default);

    /// <summary>Invites a player on the same map (founder only).</summary>
    /// <param name="connection">The inviter's connection.</param>
    /// <param name="playerId">The inviter.</param>
    /// <param name="targetMapPlayerId">The other player's id on the map.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> InviteAsync(IConnection connection, Guid playerId, uint targetMapPlayerId, CancellationToken cancellationToken = default);

    /// <summary>Answers an invitation.</summary>
    /// <param name="playerId">The invited player.</param>
    /// <param name="inviteId">The invitation.</param>
    /// <param name="accept">Whether to join.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> RespondAsync(Guid playerId, Guid inviteId, bool accept, CancellationToken cancellationToken = default);

    /// <summary>Leaves the co-op. The founder hands the role to the longest-standing member; a lone founder dissolves an empty co-op.</summary>
    /// <param name="playerId">The leaving player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> LeaveAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a member (founder only).</summary>
    /// <param name="playerId">The founder.</param>
    /// <param name="memberId">The membership to end.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> KickAsync(Guid playerId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Moves items from the player's inventory into the warehouse.</summary>
    /// <param name="playerId">The member.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> DepositAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Moves items from the warehouse into the player's inventory, as far as the player's role allows.</summary>
    /// <param name="playerId">The member.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<CoopStatus> WithdrawAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default);
}
