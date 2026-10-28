// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;

namespace Nosum.Wasm.Socket.Coop;

/// <summary>A member of the player's co-op.</summary>
/// <param name="MemberId">The membership id (not an account id).</param>
/// <param name="Role">The role.</param>
/// <param name="IsMe">Whether this is the player.</param>
/// <param name="Online">Whether the member is connected.</param>
/// <param name="Nickname">The member's nickname as the server sent it.</param>
public readonly record struct CoopMemberInfo(Guid MemberId, CoopRole Role, bool IsMe, bool Online, string Nickname);

/// <summary>One item kind in the warehouse.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many the warehouse holds.</param>
/// <param name="Available">How many the player may take out now.</param>
public readonly record struct CoopStockInfo(int ItemId, int Quantity, int Available);

/// <summary>One warehouse log line.</summary>
/// <param name="MemberId">Who; empty when that member left.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Delta">Positive for a deposit, negative for a withdrawal.</param>
/// <param name="AtMs">When, Unix milliseconds on the server clock.</param>
public readonly record struct CoopLogInfo(Guid MemberId, int ItemId, int Delta, long AtMs);

/// <summary>An invitation to the player.</summary>
/// <param name="InviteId">The invitation id.</param>
/// <param name="CoopName">The co-op's authored name (plain or i18n envelope).</param>
/// <param name="InviterNickname">The founder's nickname; empty when unknown.</param>
public readonly record struct CoopInviteInfo(Guid InviteId, string CoopName, string InviterNickname);

/// <summary>The player's co-op as the server last sent it.</summary>
/// <param name="HasCoop">Whether the player belongs to a co-op.</param>
/// <param name="Name">The authored name.</param>
/// <param name="Role">The player's role.</param>
/// <param name="Members">The members.</param>
/// <param name="Stock">The warehouse.</param>
/// <param name="Log">The latest log lines, newest first.</param>
/// <param name="Invites">Open invitations.</param>
/// <param name="WarehouseLevel">The warehouse upgrade level (docs/adr/ADR-038).</param>
/// <param name="LandLevel">The land upgrade level (docs/adr/ADR-038).</param>
[SuppressMessage("Performance", "CA1859", Justification = "Public record contract exposes IReadOnlyList.")]
public sealed record CoopInfo(bool HasCoop, string Name, CoopRole Role, IReadOnlyList<CoopMemberInfo> Members, IReadOnlyList<CoopStockInfo> Stock, IReadOnlyList<CoopLogInfo> Log, IReadOnlyList<CoopInviteInfo> Invites, int WarehouseLevel = 0, int LandLevel = 0)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly CoopInfo None = new(false, string.Empty, CoopRole.None, [], [], [], []);
}

/// <summary>The co-op's side of the connection: reads and receives the snapshot, and sends membership and warehouse requests. The server decides every outcome.</summary>
public sealed class CoopChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _pushes;

    /// <summary>Initializes the channel and starts listening for pushed snapshots.</summary>
    /// <param name="socket">The game connection.</param>
    public CoopChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _pushes = socket.Subscribe<CoopSnapshot>(this.Store);
    }

    /// <summary>Raised when the snapshot changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a request. May run on any thread.</summary>
    public event Action<CoopStatus>? Completed;

    /// <summary>Gets the player's co-op.</summary>
    public CoopInfo Current { get; private set; } = CoopInfo.None;

    /// <summary>Reads the co-op. Call whenever the connection is signed in.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Founds a co-op.</summary>
    /// <param name="name">The name as typed.</param>
    public void Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _ = this.SendAsync<CreateCoop>(r => r.Name = name);
    }

    /// <summary>Invites a player on the map.</summary>
    /// <param name="playerId">The other player's id on the map.</param>
    public void Invite(uint playerId) => _ = this.SendAsync<InvitePlayer>(r => r.TargetPlayerId = playerId);

    /// <summary>Answers an invitation.</summary>
    /// <param name="inviteId">The invitation.</param>
    /// <param name="accept">Whether to join.</param>
    public void Respond(Guid inviteId, bool accept) => _ = this.RespondAsync(inviteId, accept);

    /// <summary>Leaves the co-op.</summary>
    public void Leave() => _ = this.SendAsync<LeaveCoop>(_ => { });

    /// <summary>Removes a member.</summary>
    /// <param name="memberId">The membership.</param>
    public void Kick(Guid memberId) => _ = this.SendAsync<KickMember>(r => r.MemberId = memberId);

    /// <summary>Moves items into the warehouse.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    public void Deposit(int itemId, int quantity) => _ = this.SendAsync<DepositItem>(r =>
    {
        r.ItemId = itemId;
        r.Quantity = quantity;
    });

    /// <summary>Moves items out of the warehouse.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    public void Withdraw(int itemId, int quantity) => _ = this.SendAsync<WithdrawItem>(r =>
    {
        r.ItemId = itemId;
        r.Quantity = quantity;
    });

    /// <summary>Asks to buy the next level of an upgrade (the founder only; the server decides).</summary>
    /// <param name="track">The upgrade track.</param>
    /// <param name="targetLevel">The level to buy: one above the level shown.</param>
    public void Upgrade(CoopUpgradeTrack track, int targetLevel) => _ = this.SendAsync<UpgradeCoop>(r =>
    {
        r.Track = track;
        r.TargetLevel = (byte)targetLevel;
    });

    /// <inheritdoc/>
    public void Dispose() => _pushes.Dispose();

    private void Store(CoopSnapshot snapshot)
    {
        List<CoopMemberInfo> members = [];
        foreach (CoopMemberRow row in snapshot.Members)
        {
            members.Add(new CoopMemberInfo(row.MemberId, row.Role, row.IsMe, row.Online, row.Nickname));
        }

        List<CoopStockInfo> stock = [];
        foreach (CoopStockRow row in snapshot.Stock)
        {
            stock.Add(new CoopStockInfo(row.ItemId, row.Quantity, row.Available));
        }

        List<CoopLogInfo> log = [];
        foreach (CoopLogRow row in snapshot.Log)
        {
            log.Add(new CoopLogInfo(row.MemberId, row.ItemId, row.Delta, row.AtMs));
        }

        List<CoopInviteInfo> invites = [];
        foreach (CoopInviteRow row in snapshot.Invites)
        {
            invites.Add(new CoopInviteInfo(row.InviteId, row.CoopName, row.InviterNickname));
        }

        this.Current = new CoopInfo(snapshot.HasCoop, snapshot.Name, snapshot.Role, members, stock, log, invites, snapshot.WarehouseLevel, snapshot.LandLevel);
        this.Changed?.Invoke();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next sign-in; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetCoop> request = PacketFactory<GetCoop>.Acquire();
            CoopSnapshot snapshot = await _socket.RequestAsync<CoopSnapshot>(request.Value).ConfigureAwait(false);
            this.Store(snapshot);
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task RespondAsync(Guid inviteId, bool accept)
    {
        try
        {
            using PacketScope<RespondInvite> request = PacketFactory<RespondInvite>.Acquire();
            request.Value.InviteId = inviteId;
            request.Value.Accept = accept;
            CoopResult reply = await _socket.RequestAsync<CoopResult>(request.Value).ConfigureAwait(false);
            if (reply.Status != CoopStatus.Ok)
            {
                List<CoopInviteInfo> remaining = [];
                foreach (CoopInviteInfo invite in this.Current.Invites)
                {
                    if (invite.InviteId != inviteId)
                    {
                        remaining.Add(invite);
                    }
                }

                if (remaining.Count != this.Current.Invites.Count)
                {
                    this.Current = this.Current with { Invites = remaining.ToArray() };
                    this.Changed?.Invoke();
                }

                await this.LoadAsync().ConfigureAwait(false);
            }

            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            CoopResult reply = await _socket.RequestAsync<CoopResult>(request.Value).ConfigureAwait(false);
            if (reply.Status != CoopStatus.Ok)
            {
                await this.LoadAsync().ConfigureAwait(false);
            }

            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
