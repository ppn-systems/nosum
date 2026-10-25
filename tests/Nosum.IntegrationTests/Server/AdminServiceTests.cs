// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Admin;
using Nosum.Abstractions.Economy;
using Nosum.Contracts.Account;
using Nosum.Contracts.Admin;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Domain.Admin;
using Nosum.Domain.Social;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Admin;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The admin intents against a real PostgreSQL database (docs/adr/ADR-031 D7): who may use them, what each one changes, what it refuses and
/// what it audits. The permission tests are the point: every intent is tried by an ordinary player and by a player whose role was just taken away.
/// </summary>
public sealed class AdminServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly AccountService _accounts;
    private readonly RecordingSessions _sessions = new();
    private readonly RecordingTransfers _transfers = new();
    private readonly MuteList _mutes;
    private readonly AdminService _admin;

    public AdminServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _accounts = new AccountService(_units, new Pbkdf2PasswordHasher(iterations: 1000), new SessionTokenService(), _clock, nicknames: new NicknameDirectory(_units));
        _mutes = new MuteList(_clock);
        _admin = new AdminService(_units, _sessions, _mutes, _transfers, _clock);
    }

    public static TheoryData<string> Intents =>
    [
        "kick",
        "mute",
        "ban",
        "unban",
        "grant",
        "list-reports",
        "read-report",
    ];

    [Theory]
    [MemberData(nameof(Intents))]
    public async Task Intent_ByAnOrdinaryPlayer_IsRefusedAsNotAdminAndChangesNothing(string intent)
    {
        Account player = await CreateAsync();
        Account target = await CreateAsync();

        AdminStatus status = await RunAsync(intent, player.Id, target.Nickname);

        status.Should().Be(AdminStatus.NotAdmin);
        await AssertUntouchedAsync(target);
    }

    [Theory]
    [MemberData(nameof(Intents))]
    public async Task Intent_ByAnAdminWhoseRoleWasTakenAway_IsRefusedAtOnce(string intent)
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        await SetRoleAsync(admin, PlayerRole.Player);

        AdminStatus status = await RunAsync(intent, admin.Id, target.Nickname);

        status.Should().Be(AdminStatus.NotAdmin);
        await AssertUntouchedAsync(target);
    }

    [Fact]
    public async Task Intent_ByAnUnknownCaller_IsRefusedAsNotAdmin()
        => (await _admin.BanAsync(Guid.NewGuid(), "Anybody", CancellationToken.None)).Should().Be(AdminStatus.NotAdmin);

    [Fact]
    public async Task Ban_ByAnAdmin_MarksTheAccountEndsTheConnectionAndBlocksLoginAndResume()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _sessions.Online.Add(target.Id);

        AdminStatus status = await _admin.BanAsync(admin.Id, target.Nickname, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        (await LoadAsync(target)).BannedAt.Should().Be(s_start);
        _sessions.Ended.Should().Equal((target.Id, AccountStatus.Banned));
        (await _accounts.LoginAsync(target.Email, "password-1", CancellationToken.None)).Status.Should().Be(AccountStatus.Banned);
        (await _accounts.ResumeAsync(target.Token, CancellationToken.None)).Status.Should().Be(AccountStatus.Banned);
    }

    [Fact]
    public async Task Ban_WrongPassword_DoesNotRevealTheBan()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _ = await _admin.BanAsync(admin.Id, target.Nickname, CancellationToken.None);

        AccountOutcome outcome = await _accounts.LoginAsync(target.Email, "not-the-password", CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.InvalidCredentials);
    }

    [Fact]
    public async Task Unban_ByAnAdmin_LetsThePlayerSignInAgain()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _ = await _admin.BanAsync(admin.Id, target.Nickname, CancellationToken.None);

        AdminStatus status = await _admin.UnbanAsync(admin.Id, target.Nickname, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        (await LoadAsync(target)).BannedAt.Should().BeNull();
        (await _accounts.LoginAsync(target.Email, "password-1", CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task Mute_ForHours_MutesUntilItEndsAndSurvivesARestart()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();

        AdminStatus status = await _admin.MuteAsync(admin.Id, target.Nickname, 4, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        _mutes.IsMuted(target.Id).Should().BeTrue();
        (await LoadAsync(target)).MutedUntil.Should().Be(s_start.AddHours(4));

        // A restarted server loads the running mutes from the database.
        MuteList restarted = new(_clock);
        await restarted.LoadAsync(_units, CancellationToken.None);
        restarted.IsMuted(target.Id).Should().BeTrue();

        _clock.Advance(TimeSpan.FromHours(4));
        _mutes.IsMuted(target.Id).Should().BeFalse("the mute ends by the clock");
        restarted.IsMuted(target.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Mute_ZeroHours_LiftsTheMute()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _ = await _admin.MuteAsync(admin.Id, target.Nickname, 4, CancellationToken.None);

        AdminStatus status = await _admin.MuteAsync(admin.Id, target.Nickname, 0, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        _mutes.IsMuted(target.Id).Should().BeFalse();
        (await LoadAsync(target)).MutedUntil.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AdminLimits.MaxMuteHours + 1)]
    public async Task Mute_OutOfRangeHours_IsRefusedAndChangesNothing(int hours)
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();

        AdminStatus status = await _admin.MuteAsync(admin.Id, target.Nickname, hours, CancellationToken.None);

        status.Should().Be(AdminStatus.InvalidDuration);
        await AssertUntouchedAsync(target);
    }

    [Fact]
    public async Task Kick_ConnectedPlayer_EndsTheConnectionWithTheKickedReason()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _sessions.Online.Add(target.Id);

        AdminStatus status = await _admin.KickAsync(admin.Id, target.Nickname, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        _sessions.Ended.Should().Equal((target.Id, AccountStatus.SessionKicked));
        (await LoadAsync(target)).BannedAt.Should().BeNull("a kick is not a ban");
    }

    [Fact]
    public async Task Kick_PlayerNotConnected_ReportsNotOnline()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();

        (await _admin.KickAsync(admin.Id, target.Nickname, CancellationToken.None)).Should().Be(AdminStatus.NotOnline);
    }

    [Fact]
    public async Task KickMuteBan_AgainstOneselfOrAnotherAdmin_AreRefused()
    {
        Account admin = await CreateAdminAsync();
        Account other = await CreateAdminAsync();

        (await _admin.KickAsync(admin.Id, admin.Nickname, CancellationToken.None)).Should().Be(AdminStatus.SelfTarget);
        (await _admin.MuteAsync(admin.Id, admin.Nickname, 1, CancellationToken.None)).Should().Be(AdminStatus.SelfTarget);
        (await _admin.BanAsync(admin.Id, admin.Nickname, CancellationToken.None)).Should().Be(AdminStatus.SelfTarget);
        (await _admin.BanAsync(admin.Id, other.Nickname, CancellationToken.None)).Should().Be(AdminStatus.TargetIsAdmin);
        (await _admin.KickAsync(admin.Id, other.Nickname, CancellationToken.None)).Should().Be(AdminStatus.TargetIsAdmin);
        (await LoadAsync(other)).BannedAt.Should().BeNull();
        _sessions.Ended.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("bad-name")]
    public async Task Intent_WithAnInvalidNickname_IsRefused(string nickname)
    {
        Account admin = await CreateAdminAsync();

        (await _admin.BanAsync(admin.Id, nickname, CancellationToken.None)).Should().Be(AdminStatus.InvalidNickname);
    }

    [Fact]
    public async Task Intent_WithAnUnknownNickname_IsRefused()
    {
        Account admin = await CreateAdminAsync();

        (await _admin.BanAsync(admin.Id, TestNicknames.Next(), CancellationToken.None)).Should().Be(AdminStatus.UnknownPlayer);
    }

    [Fact]
    public async Task Grant_ItemAndCoins_GoesThroughTheTransferServiceToTheTarget()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        int itemId = ItemCatalog.All.First(i => i.MaxStack >= 3).Id;

        AdminStatus status = await _admin.GrantAsync(admin.Id, target.Nickname, itemId, 3, 500, CancellationToken.None);

        status.Should().Be(AdminStatus.Ok);
        _transfers.Calls.Should().ContainSingle().Which.Should().Be((TransferParty.Player(target.Id), itemId, 3, 500L, "admin.grant"));
    }

    [Fact]
    public async Task Grant_CoinsOnly_Works()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();

        (await _admin.GrantAsync(admin.Id, target.Nickname, 0, 0, 100, CancellationToken.None)).Should().Be(AdminStatus.Ok);

        _transfers.Calls.Should().ContainSingle().Which.Should().Be((TransferParty.Player(target.Id), 0, 0, 100L, "admin.grant"));
    }

    [Fact]
    public async Task Grant_TransferRefused_ReportsGrantFailed()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        _transfers.Result = TransferStatus.NoSpace;

        (await _admin.GrantAsync(admin.Id, target.Nickname, ItemCatalog.All[0].Id, 1, 0, CancellationToken.None)).Should().Be(AdminStatus.GrantFailed);
    }

    [Theory]
    [InlineData(-5, 1, 0L, AdminStatus.InvalidItem)]
    [InlineData(999_999, 1, 0L, AdminStatus.InvalidItem)]
    [InlineData(0, 0, 0L, AdminStatus.InvalidItem)]
    [InlineData(0, 0, -1L, AdminStatus.InvalidQuantity)]
    [InlineData(0, 0, 1_000_000_001L, AdminStatus.InvalidQuantity)]
    public async Task Grant_OutOfRange_IsRefusedAndMovesNothing(int itemId, int quantity, long coins, AdminStatus expected)
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();

        (await _admin.GrantAsync(admin.Id, target.Nickname, itemId, quantity, coins, CancellationToken.None)).Should().Be(expected);

        _transfers.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_MoreThanOneStack_IsRefused()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        ItemDefinition item = ItemCatalog.All.First(i => i.MaxStack >= 2);

        (await _admin.GrantAsync(admin.Id, target.Nickname, item.Id, item.MaxStack + 1, 0, CancellationToken.None)).Should().Be(AdminStatus.InvalidQuantity);

        _transfers.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EverySuccessfulAction_WritesOneAuditRow_AndARefusalWritesNone()
    {
        Account admin = await CreateAdminAsync();
        Account target = await CreateAsync();
        await ClearAuditAsync();
        _sessions.Online.Add(target.Id);

        _ = await _admin.KickAsync(admin.Id, target.Nickname, CancellationToken.None);
        _ = await _admin.MuteAsync(admin.Id, target.Nickname, 2, CancellationToken.None);
        _ = await _admin.BanAsync(admin.Id, target.Nickname, CancellationToken.None);
        _ = await _admin.UnbanAsync(admin.Id, target.Nickname, CancellationToken.None);
        _ = await _admin.GrantAsync(admin.Id, target.Nickname, 0, 0, 10, CancellationToken.None);
        _ = await _admin.BanAsync(admin.Id, admin.Nickname, CancellationToken.None);
        _ = await _admin.MuteAsync(admin.Id, target.Nickname, -1, CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        List<AdminAuditEntry> rows = await db.AdminAudit.Where(a => a.AdminId == admin.Id).OrderBy(a => a.CreatedAt).ThenBy(a => a.Action).ToListAsync();
        rows.Select(r => (AdminAction)r.Action).Should().BeEquivalentTo([AdminAction.Kick, AdminAction.Mute, AdminAction.Ban, AdminAction.Unban, AdminAction.Grant]);
        rows.Should().OnlyContain(r => r.TargetId == target.Id && r.CreatedAt == s_start);
        rows.Single(r => r.Action == (byte)AdminAction.Mute).Detail.Should().Be("hours=2");
        rows.Single(r => r.Action == (byte)AdminAction.Grant).Detail.Should().Be("item=0 qty=0 coins=10");
    }

    [Fact]
    public async Task ListReports_ByAnAdmin_ReturnsTheNewestFirstAndReadReportReturnsTheLinesInOrder()
    {
        Account admin = await CreateAdminAsync();
        Account reporter = await CreateAsync();
        Account reported = await CreateAsync();
        Guid older = await AddReportAsync(reporter, reported, s_start.AddMinutes(-10), "first", "second");
        Guid newer = await AddReportAsync(reporter, reported, s_start.AddMinutes(-1), "only");

        AdminReadResult<AdminReportSummary> list = await _admin.ListReportsAsync(admin.Id, CancellationToken.None);
        AdminReadResult<AdminReportLine> lines = await _admin.ReadReportAsync(admin.Id, older, CancellationToken.None);

        list.Status.Should().Be(AdminStatus.Ok);
        list.Items.Select(r => r.Id).Take(2).Should().Equal(newer, older);
        list.Items[0].Should().Match<AdminReportSummary>(r => r.Reporter == reporter.Nickname && r.Reported == reported.Nickname);
        lines.Status.Should().Be(AdminStatus.Ok);
        lines.Items.Select(l => l.Text).Should().Equal("first", "second");
    }

    [Fact]
    public async Task ReadReport_UnknownId_IsRefused()
    {
        Account admin = await CreateAdminAsync();

        AdminReadResult<AdminReportLine> result = await _admin.ReadReportAsync(admin.Id, Guid.NewGuid(), CancellationToken.None);

        result.Status.Should().Be(AdminStatus.UnknownReport);
        result.Items.Should().BeEmpty();
    }

    // ---- helpers ----

    private Task<AdminStatus> RunAsync(string intent, Guid caller, string nickname) => intent switch
    {
        "kick" => _admin.KickAsync(caller, nickname),
        "mute" => _admin.MuteAsync(caller, nickname, 3),
        "ban" => _admin.BanAsync(caller, nickname),
        "unban" => _admin.UnbanAsync(caller, nickname),
        "grant" => _admin.GrantAsync(caller, nickname, 0, 0, 10),
        "list-reports" => this.StatusOfAsync(_admin.ListReportsAsync(caller)),
        "read-report" => this.StatusOfAsync(_admin.ReadReportAsync(caller, Guid.NewGuid())),
        _ => throw new ArgumentOutOfRangeException(nameof(intent)),
    };

    private async Task<AdminStatus> StatusOfAsync<T>(Task<AdminReadResult<T>> read) => (await read).Status;

    // Nothing the intent could have changed was changed, and nothing was audited, ended or moved.
    private async Task AssertUntouchedAsync(Account target)
    {
        Player player = await LoadAsync(target);
        player.BannedAt.Should().BeNull();
        player.MutedUntil.Should().BeNull();
        _mutes.IsMuted(target.Id).Should().BeFalse();
        _sessions.Ended.Should().BeEmpty();
        _transfers.Calls.Should().BeEmpty();
        await using NosumDbContext db = new(_database.Options);
        (await db.AdminAudit.AnyAsync(a => a.TargetId == target.Id)).Should().BeFalse();
    }

    private async Task<Account> CreateAsync()
    {
        string email = "a" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        AccountOutcome outcome = await _accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, email, outcome.SessionToken, outcome.Nickname);
    }

    private async Task<Account> CreateAdminAsync()
    {
        Account account = await CreateAsync();
        await SetRoleAsync(account, PlayerRole.Admin);
        return account;
    }

    private async Task SetRoleAsync(Account account, PlayerRole role)
    {
        await using NosumDbContext db = new(_database.Options);
        _ = await db.Players.Where(p => p.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Role, role));
    }

    private async Task<Player> LoadAsync(Account account)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.Players.AsNoTracking().SingleAsync(p => p.Id == account.Id);
    }

    private async Task ClearAuditAsync()
    {
        await using NosumDbContext db = new(_database.Options);
        _ = await db.AdminAudit.ExecuteDeleteAsync();
    }

    private async Task<Guid> AddReportAsync(Account reporter, Account reported, DateTimeOffset at, params string[] texts)
    {
        await using NosumDbContext db = new(_database.Options);
        ChatReport report = new() { ReporterId = reporter.Id, ReportedId = reported.Id, CreatedAt = at };
        db.ChatReports.Add(report);
        for (int i = 0; i < texts.Length; i++)
        {
            db.ChatReportLines.Add(new ChatReportLine { ReportId = report.Id, Sequence = i, Channel = 0, SenderId = reported.Id, SenderNickname = reported.Nickname, Text = texts[i], SentAt = at });
        }

        _ = await db.SaveChangesAsync();
        return report.Id;
    }

    private sealed record Account(Guid Id, string Email, string Token, string Nickname);

    private sealed class RecordingSessions : ISessionRegistry
    {
        public HashSet<Guid> Online { get; } = [];

        public List<(Guid PlayerId, AccountStatus Reason)> Ended { get; } = [];

        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public IConnection? GetActive(Guid playerId) => null;

        public ValueTask<bool> EndAsync(Guid playerId, AccountStatus reason, CancellationToken cancellationToken = default)
        {
            if (!this.Online.Remove(playerId))
            {
                return ValueTask.FromResult(false);
            }

            this.Ended.Add((playerId, reason));
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingTransfers : ITransferService
    {
        public List<(TransferParty Party, int ItemId, int Items, long Coins, string Reason)> Calls { get; } = [];

        public TransferStatus Result { get; set; } = TransferStatus.Ok;

        public Task<TransferStatus> ExecuteAsync(IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default)
        {
            TransferLeg leg = legs[0];
            this.Calls.Add((leg.Party, leg.ItemId, leg.Items, leg.Coins, reason));
            return Task.FromResult(this.Result);
        }

        public Task<long> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }
}
