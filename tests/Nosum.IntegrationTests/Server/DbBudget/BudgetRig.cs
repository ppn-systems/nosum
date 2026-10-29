// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Host;
using Nosum.Abstractions.Inventory;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Friends;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Quests;
using Nosum.Infrastructure.Visits;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// The real host over a free port and a real PostgreSQL database whose every command is counted by a <see cref="DbCommandCounter"/>
/// (docs/adr/ADR-035). Time is a fake clock, so a measurement does not depend on when it runs.
/// </summary>
internal sealed class BudgetRig : IAsyncDisposable
{
    // Every client of a run shares one address and the handlers rate limit per address: keep every request this far apart.
    private static readonly TimeSpan s_requestGap = TimeSpan.FromMilliseconds(260);
    private static readonly SemaphoreSlim s_gate = new(1, 1);
    private static DateTime s_lastRequest = DateTime.MinValue;

    private readonly NetworkApplication _app;
    private readonly CancellationTokenSource _timeout;
    private readonly HashSet<Guid> _watched = [];

    private BudgetRig(NetworkApplication app, ushort port, DbCommandCounter counter, FakeTimeProvider clock, DbContextOptions<NosumDbContext> options, TimeSpan timeout)
    {
        _app = app;
        this.Port = port;
        this.Counter = counter;
        this.Clock = clock;
        this.Options = options;
        _timeout = new CancellationTokenSource(timeout);
    }

    /// <summary>Gets the WebSocket port.</summary>
    public ushort Port { get; }

    /// <summary>Gets the counter every database command of the host passes.</summary>
    public DbCommandCounter Counter { get; }

    /// <summary>Gets the server clock.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Gets the database options the host runs on (with the counter attached).</summary>
    public DbContextOptions<NosumDbContext> Options { get; }

    /// <summary>Gets where the in-memory state of a player differed from the rows after a measured row (docs/adr/ADR-035); empty when the caches are coherent.</summary>
    public List<string> Divergences { get; } = [];

    /// <summary>Gets how many held copies were compared with the rows; a check that compared nothing proves nothing.</summary>
    public int Comparisons { get; private set; }

    /// <summary>Starts comparing the in-memory state of a signed-in player with the rows after every measured row.</summary>
    /// <param name="accountId">The player's account id.</param>
    public void Watch(Guid accountId)
    {
        lock (_watched)
        {
            _ = _watched.Add(accountId);
        }
    }

    /// <summary>Gets everything measured so far, in the order it ran.</summary>
    public List<Measurement> Results { get; } = [];

    /// <summary>Gets a token that ends the run.</summary>
    public CancellationToken Timeout => _timeout.Token;

    /// <summary>Gets the service the host registered under <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service interface.</typeparam>
    /// <returns>The registered instance.</returns>
    public static T Service<T>()
        where T : class
        => InstanceManager.Instance.GetExistingInstance<T>() ?? throw new InvalidOperationException(typeof(T).Name + " is not registered.");

    /// <summary>Starts the host on counted options over the fixture's database.</summary>
    /// <param name="database">The migrated test database.</param>
    /// <param name="start">The fake clock's first instant.</param>
    /// <param name="timeout">How long the whole run may take.</param>
    /// <returns>The running rig.</returns>
    public static async Task<BudgetRig> StartAsync(PostgresFixture database, DateTimeOffset start, TimeSpan timeout)
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        ushort port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        DbCommandCounter counter = new();
        DbContextOptions<NosumDbContext> options = new DbContextOptionsBuilder<NosumDbContext>()
            .UseNpgsql(database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;
        FakeTimeProvider clock = new(start);

        HostServices.Register(options);
        NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        BudgetRig rig = new(app, port, counter, clock, options, timeout);
        await app.ActivateAsync(rig.Timeout);
        return rig;
    }

    /// <summary>Opens a connected, handshaken socket that is not signed in.</summary>
    /// <returns>The anonymous client.</returns>
    public async Task<BudgetPlayer> ConnectAsync()
    {
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = this.Port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        await session.ConnectAsync(ct: this.Timeout);
        await session.HandshakeAsync(this.Timeout);
        return new BudgetPlayer(this, session);
    }

    /// <summary>
    /// Creates a guest straight through the account service (sign-ups are rate limited per address), then signs a new socket in
    /// with its token over the wire. Nothing of this is measured.
    /// </summary>
    /// <returns>A signed-in player who has not joined a map.</returns>
    public async Task<BudgetPlayer> SignedInGuestAsync()
    {
        AccountOutcome created = await Service<IAccountService>().StartGuestAsync(GameLanguage.English, this.Timeout);
        BudgetPlayer player = await this.ConnectAsync();
        await player.ResumeAsync(created.SessionToken!);
        return player;
    }

    /// <summary>Creates a registered account straight through the account service (a guest that links a Google identity with a nickname).</summary>
    /// <param name="nickname">The wanted nickname.</param>
    /// <param name="subject">The Google subject the account is linked to; signing in with the same subject reaches this account.</param>
    /// <returns>The session token.</returns>
    public async Task<string> RegisteredAccountAsync(string nickname, string subject)
    {
        AccountOutcome created = await TestAccounts.RegisterAsync(Service<IAccountService>(), subject, nickname, GameLanguage.English, this.Timeout);
        if (created.Status != AccountStatus.Ok)
        {
            throw new InvalidOperationException("Could not register " + nickname + ": " + created.Status);
        }

        return created.SessionToken!;
    }

    /// <summary>
    /// Runs <paramref name="act"/> and records what it cost the database: the counter is let go quiet first, so earlier work does not leak in,
    /// and again afterwards, so work a request leaves running after its reply (a push, a save) is counted for it.
    /// </summary>
    /// <typeparam name="T">What <paramref name="act"/> returns.</typeparam>
    /// <param name="group">The area (Account, Farm, Market, ...) the row is filed under.</param>
    /// <param name="opcode">The client packet's opcode, or <see langword="null"/> for a cost that is not a packet.</param>
    /// <param name="name">The packet or event name.</param>
    /// <param name="variant">What the measured call did (the case; the same packet may have several rows).</param>
    /// <param name="act">The work to measure.</param>
    /// <param name="keep">Tells whether the result is worth a row (a try that was refused is not); all rows by default.</param>
    /// <returns>What <paramref name="act"/> returned.</returns>
    public async Task<T> MeasureAsync<T>(string group, ushort? opcode, string name, string variant, Func<Task<T>> act, Func<T, bool>? keep = null)
    {
        await this.Counter.SettleAsync(TimeSpan.FromMilliseconds(120), this.Timeout);
        int mark = this.Counter.Mark;
        T value = await act();
        await this.Counter.SettleAsync(TimeSpan.FromMilliseconds(220), this.Timeout);
        if (keep is not null && !keep(value))
        {
            return value;
        }

        string? outcome = value?.GetType().GetProperty("Status")?.GetValue(value)?.ToString();
        this.Results.Add(new Measurement(group, opcode, name, variant, this.Counter.Since(mark)) { Outcome = outcome });

        // A write-behind row (docs/adr/ADR-035 D7) would otherwise land inside a later row's window when its timer fires; it is flushed here, after this row's count was taken,
        // and its own cost is measured in the WriteBehindFlush row.
        await Service<Nosum.Infrastructure.Caching.WriteBehind>().FlushAllAsync(this.Timeout);
        await this.CheckCachesAsync(name + " / " + variant);
        return value;
    }

    // After a row has been measured and its delayed writes were made: whatever a cache holds of a watched player must equal what a service with nothing in memory reads
    // from the rows. Caches that hold nothing for a player are not asked (asking would load them and change what later rows cost).
    private async Task CheckCachesAsync(string row)
    {
        Guid[] accounts;
        lock (_watched)
        {
            accounts = [.. _watched];
        }

        EfUnitOfWorkFactory units = new(this.Options);
        InventoryService inventories = new(units, this.Clock);
        PlayerStateCache states = new();
        QuestService quests = new(units, this.Clock);
        CoopDirectory coops = new(units, cached: false);
        FriendFacts friends = new(units, cached: false);
        VisitFacts visits = new(units, cached: false);
        NicknameDirectory names = new(units, cacheFacts: false);
        foreach (Guid account in accounts)
        {
            await this.CompareAsync(row, "inventory", account, ((InventoryService)Service<IInventoryService>()).DescribeHeld(account), async () => InventoryService.Describe(await inventories.GetAsync(account, this.Timeout)));
            await this.CompareAsync(row, "saved place, energy and experience", account, Service<PlayerStateCache>().DescribeHeld(account), async () => await states.DescribeAsync(units, account, this.Timeout));
            await this.CompareAsync(row, "quests", account, ((QuestService)Service<Nosum.Abstractions.Quests.IQuestService>()).DescribeHeld(account), async () => await quests.DescribeAsync(account, this.Timeout));
            await this.CompareAsync(row, "co-op", account, Service<CoopDirectory>().DescribeHeld(account), async () => await coops.DescribeAsync(account, this.Timeout));
            await this.CompareAsync(row, "friends", account, Service<FriendFacts>().DescribeHeld(account), async () => await friends.DescribeAsync(account, this.Timeout));
            await this.CompareAsync(row, "visit settings", account, Service<VisitFacts>().DescribeHeld(account), async () => await visits.DescribeAsync(account, this.Timeout));
            await this.CompareAsync(row, "player facts", account, ((NicknameDirectory)Service<INicknameDirectory>()).DescribeHeld(account), async () => await names.DescribeAsync(account, this.Timeout));
        }
    }

    private async Task CompareAsync(string row, string what, Guid account, string? held, Func<Task<string>> rows)
    {
        if (held is null)
        {
            return;
        }

        // A cache may hold only the part it needs (friends without requests): every held part must be the rows' part.
        string fromRows = await rows();
        this.Comparisons++;
        HashSet<string> inRows = [.. fromRows.Split(';', StringSplitOptions.RemoveEmptyEntries)];
        if (!held.Split(';', StringSplitOptions.RemoveEmptyEntries).All(inRows.Contains))
        {
            this.Divergences.Add($"after {row}: {what} of {account} is [{held}] in memory but [{fromRows}] in the rows");
        }
    }

    /// <summary>Records a packet that was not measured and why (a closed feature, a tester tool).</summary>
    /// <param name="group">The area.</param>
    /// <param name="opcode">The client packet's opcode.</param>
    /// <param name="name">The packet name.</param>
    /// <param name="reason">Why there is no number.</param>
    public void Skip(string group, ushort opcode, string name, string reason)
        => this.Results.Add(new Measurement(group, opcode, name, "not measured", DbCounts.None) { Skipped = reason });

    /// <summary>Stands a joined player at a point of the place they are in, as if they had walked there.</summary>
    /// <param name="account">The player's account id.</param>
    /// <param name="x">Feet x, pixels.</param>
    /// <param name="y">Feet y, pixels.</param>
    public static void Stand(Guid account, double x, double y)
    {
        Nalix.Abstractions.Networking.IConnection connection = Service<ISessionRegistry>().GetActive(account) ?? throw new InvalidOperationException("Not signed in.");
        if (!Service<Nosum.Abstractions.Presence.IMapPresence>().TryGet(connection, out Nosum.Abstractions.Presence.PlayerPresence? player))
        {
            throw new InvalidOperationException("Not on a map.");
        }

        double now = TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;
        lock (player.Gate)
        {
            player.Motion = new Nosum.Gameplay.Movement.MotionState(x, y, now, (int)Nosum.Core.Facing.Up);
        }
    }

    /// <summary>Moves a joined player to another instance on the server's side, as a visit or a door would.</summary>
    /// <param name="account">The player's account id.</param>
    /// <param name="destination">Where to.</param>
    /// <returns>A task that completes on arrival.</returns>
    public static async Task TravelAsync(Guid account, Nosum.Gameplay.Maps.MapInstance destination)
    {
        Nalix.Abstractions.Networking.IConnection connection = Service<ISessionRegistry>().GetActive(account) ?? throw new InvalidOperationException("Not signed in.");
        Nosum.Abstractions.Player.TravelStatus status = await Service<Nosum.Abstractions.Player.IPlayerService>().TravelAsync(connection, destination);
        if (status != Nosum.Abstractions.Player.TravelStatus.Arrived)
        {
            throw new InvalidOperationException("Travel refused: " + status);
        }
    }

    /// <summary>Waits out the gap between two requests of the run and holds the shared pace.</summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A task that completes when the next request may go out.</returns>
    public static async Task PaceAsync(CancellationToken ct)
    {
        await s_gate.WaitAsync(ct);
        try
        {
            TimeSpan wait = s_requestGap - (DateTime.UtcNow - s_lastRequest);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _ = s_gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.DeactivateAsync(CancellationToken.None);
        await _app.DisposeAsync();
        _timeout.Dispose();
    }
}

/// <summary>One real WebSocket client of the budget run.</summary>
internal sealed class BudgetPlayer : IAsyncDisposable
{
    private readonly BudgetRig _rig;
    private readonly WebSocketSession _session;
    private uint _moveSequence;

    public BudgetPlayer(BudgetRig rig, WebSocketSession session)
    {
        _rig = rig;
        _session = session;
    }

    /// <summary>Gets the account id (the key of every table).</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Gets the display nickname the sign-in reported.</summary>
    public string Nickname { get; private set; } = string.Empty;

    /// <summary>Gets the id on the map after <see cref="JoinAsync"/>.</summary>
    public uint MapPlayerId { get; private set; }

    /// <summary>Gets the session token of the sign-in.</summary>
    public string SessionToken { get; private set; } = string.Empty;

    /// <summary>Gets where the player stands after the join and every accepted move, in pixels.</summary>
    public (float X, float Y) Feet { get; private set; }

    /// <summary>Sets where the next step starts from, after the server was told to stand the player elsewhere.</summary>
    /// <param name="x">Feet x.</param>
    /// <param name="y">Feet y.</param>
    public void SetFeet(float x, float y) => this.Feet = (x, y);

    /// <summary>Gets the next move sequence number.</summary>
    /// <returns>A sequence number.</returns>
    public uint NextSequence() => ++_moveSequence;

    /// <summary>Gets the underlying session, for subscriptions.</summary>
    public WebSocketSession Session => _session;

    /// <summary>Sends a request and waits for its typed reply, after the shared pace.</summary>
    /// <typeparam name="TResponse">The reply type.</typeparam>
    /// <param name="request">The request packet.</param>
    /// <returns>The reply.</returns>
    public async Task<TResponse> RequestAsync<TResponse>(IPacket request)
        where TResponse : class, IPacket, IPacketStaticOpcode
    {
        await BudgetRig.PaceAsync(_rig.Timeout);
        return await RequestExtensions.RequestAsync<TResponse>(_session, request, RequestOptions.Default.WithEncrypt().WithTimeout(8000), ct: _rig.Timeout);
    }

    /// <summary>Sends a request that has no reply, after the shared pace.</summary>
    /// <param name="request">The request packet.</param>
    /// <returns>A task that completes when the frame was sent.</returns>
    public async Task SendAsync(IPacket request)
    {
        await BudgetRig.PaceAsync(_rig.Timeout);
        await _session.SendAsync(request, _rig.Timeout);
    }

    /// <summary>Sends a request and returns its reply, disposing the request.</summary>
    /// <typeparam name="TResponse">The reply type.</typeparam>
    /// <param name="request">The request packet.</param>
    /// <returns>The reply.</returns>
    public async Task<TResponse> RequestAsyncOf<TResponse>(IPacket request)
        where TResponse : class, IPacket, IPacketStaticOpcode
    {
        try
        {
            return await this.RequestAsync<TResponse>(request);
        }
        finally
        {
            (request as IDisposable)?.Dispose();
        }
    }

    /// <summary>Signs in with a token (the client's resume path).</summary>
    /// <param name="token">The session token.</param>
    /// <returns>The reply.</returns>
    public async Task<AccountResult> ResumeAsync(string token)
    {
        using ResumeSession request = new() { SessionToken = token };
        AccountResult result = await this.RequestAsync<AccountResult>(request);
        if (result.Status != AccountStatus.Ok)
        {
            throw new InvalidOperationException("Resume refused: " + result.Status);
        }

        this.AccountId = result.PlayerId;
        _rig.Watch(result.PlayerId);
        this.Nickname = result.Nickname;
        this.SessionToken = token;
        return result;
    }

    /// <summary>Joins the map the server picks (the saved one, or the farm).</summary>
    /// <param name="mapId">The map asked for.</param>
    /// <returns>The reply.</returns>
    public async Task<JoinMapResult> JoinAsync(string mapId)
    {
        using JoinMap request = new() { MapId = mapId };
        JoinMapResult result = await this.RequestAsync<JoinMapResult>(request);
        if (result.Status != JoinStatus.Ok)
        {
            throw new InvalidOperationException("Join refused: " + result.Status);
        }

        this.MapPlayerId = result.PlayerId;
        this.Feet = (result.SpawnX, result.SpawnY);
        return result;
    }

    /// <summary>Claims a position one legal step from the last one (the real client reports about every 120 ms).</summary>
    /// <param name="dx">Pixels east.</param>
    /// <param name="dy">Pixels south.</param>
    /// <returns>A task that completes when the claim was sent.</returns>
    public async Task MoveByAsync(float dx, float dy)
    {
        this.Feet = (this.Feet.X + dx, this.Feet.Y + dy);
        using MoveIntent intent = new() { Sequence = ++_moveSequence, X = this.Feet.X, Y = this.Feet.Y, Facing = 1, Moving = true };
        await this.SendAsync(intent);
    }

    /// <summary>Signs the socket out and closes it.</summary>
    /// <returns>A task that completes when the socket is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _session.DisconnectAsync();
        }
        finally
        {
            _session.Dispose();
        }
    }
}

/// <summary>One measured cost.</summary>
/// <param name="Group">The area the row is filed under.</param>
/// <param name="Opcode">The client packet's opcode, or null for a cost that is not a packet.</param>
/// <param name="Name">The packet or event name.</param>
/// <param name="Variant">What the measured call did.</param>
/// <param name="Counts">What it cost the database.</param>
internal sealed record Measurement(string Group, ushort? Opcode, string Name, string Variant, DbCounts Counts)
{
    /// <summary>Gets the status the reply carried (a refusal costs less than the real thing, so the report shows which one was measured), or null.</summary>
    public string? Outcome { get; init; }

    /// <summary>Gets why there is no number for this row, or null.</summary>
    public string? Skipped { get; init; }

    /// <summary>Gets the key a budget names the row by: the packet name and the case.</summary>
    public string Key => this.Name + " / " + this.Variant;
}

/// <summary>Helpers shared by the scenarios.</summary>
internal static class BudgetPlayerExtensions
{
    /// <summary>Sends a request, measured, and returns its reply.</summary>
    /// <typeparam name="TResponse">The reply type.</typeparam>
    /// <param name="rig">The rig.</param>
    /// <param name="player">The sender.</param>
    /// <param name="group">The area.</param>
    /// <param name="opcode">The packet's opcode.</param>
    /// <param name="name">The packet name.</param>
    /// <param name="variant">The case.</param>
    /// <param name="request">Makes the request (disposed here).</param>
    /// <returns>The reply.</returns>
    public static async Task<TResponse> ProbeAsync<TResponse>(this BudgetRig rig, BudgetPlayer player, string group, ushort opcode, string name, string variant, Func<IPacket> request, Func<TResponse, bool>? keep = null)
        where TResponse : class, IPacket, IPacketStaticOpcode
    {
        IPacket packet = request();
        try
        {
            return await rig.MeasureAsync(group, opcode, name, variant, () => player.RequestAsync<TResponse>(packet), keep);
        }
        finally
        {
            (packet as IDisposable)?.Dispose();
        }
    }
}
