// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Mail;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The mailbox against a real PostgreSQL database (docs/adr/ADR-029 D5): an attachment is handed over exactly once even when the same mail is
/// collected many times at once, a failed hand-over leaves the mail collectible, expired mail is invisible and then removed, and nobody can
/// reach another player's mail.
/// </summary>
public sealed class MailServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly RecordingNotifier _notifier = new();
    private readonly MailService _mail;

    public MailServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _mail = new MailService(_units, _transfers, _notifier, _clock);
    }

    [Fact]
    public async Task Send_ThenList_ShowsTheMailNewestFirstWithItsAttachmentAndCodeArguments()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _mail.SendAsync(player, MailRules.Codes.MarketSold, ["200", "10", "190"]);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Guid second = await _mail.SendAsync(player, MailRules.Codes.GoalReward, ["spring"], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 3), new MailItem(ItemCatalog.Parsnip, 2), new MailItem(ItemCatalog.Radish, 1)], 500));

        IReadOnlyList<MailView> mails = await _mail.ListAsync(player);

        mails.Should().HaveCount(2);
        mails[0].Id.Should().Be(second, "newest first");
        mails[0].Items.Should().Equal(new MailItem(ItemCatalog.Parsnip, 5), new MailItem(ItemCatalog.Radish, 1));
        mails[0].Coins.Should().Be(500);
        mails[0].Collected.Should().BeFalse();
        mails[0].ExpiresAt.Should().Be(s_start.AddMinutes(5).AddDays(MailRules.ExpiryDays));
        mails[1].Code.Should().Be("market.sold");
        mails[1].Args.Should().Equal("200", "10", "190");
        mails[1].Read.Should().BeFalse();
        (await _mail.CountUnreadAsync(player)).Should().Be(2);
    }

    [Fact]
    public async Task Send_OverlongArgumentsAndTooManyOfThem_AreClipped()
    {
        Guid player = await CreatePlayerAsync();
        string[] args = [.. Enumerable.Repeat(new string('x', MailRules.MaxArgLength + 50), MailRules.MaxArgs + 3)];

        _ = await _mail.SendAsync(player, "test.code", args);

        MailView mail = (await _mail.ListAsync(player)).Single();
        mail.Args.Should().HaveCount(MailRules.MaxArgs);
        mail.Args.Should().OnlyContain(a => a.Length == MailRules.MaxArgLength);
    }

    [Fact]
    public async Task Send_AnAttachmentOfAnUnknownItem_IsRefusedBeforeAnythingIsStored()
    {
        Guid player = await CreatePlayerAsync();

        Func<Task> send = () => _mail.SendAsync(player, "test.code", [], new MailAttachments([new MailItem(999_999, 1)]));

        await send.Should().ThrowAsync<ArgumentException>();
        (await _mail.ListAsync(player)).Should().BeEmpty();
    }

    [Fact]
    public async Task Send_PushesTheUnreadCount_AndReadingLowersIt()
    {
        Guid player = await CreatePlayerAsync();
        Guid first = await _mail.SendAsync(player, "test.one", []);
        _ = await _mail.SendAsync(player, "test.two", []);

        _notifier.UnreadOf(player).Should().Equal(1, 2);

        (await _mail.ReadAsync(player, first)).Should().Be(MailStatus.Ok);
        (await _mail.ReadAsync(player, first)).Should().Be(MailStatus.Ok, "reading again changes nothing");

        _notifier.UnreadOf(player).Last().Should().Be(1);
        (await _mail.CountUnreadAsync(player)).Should().Be(1);
    }

    [Fact]
    public async Task Collect_HandsOverItemsAndCoinsOnce_ThenRefusesTheSecondCollect()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        Guid mail = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 7)], 250));

        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.Ok);
        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.AlreadyCollected);

        (await ParsnipsOfAsync(player)).Should().Be(7);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + 250);
        MailView view = (await _mail.ListAsync(player)).Single();
        view.Collected.Should().BeTrue();
        view.Read.Should().BeTrue("collecting reads the mail");
        _notifier.UnreadOf(player).Last().Should().Be(0);
    }

    [Fact]
    public async Task Collect_TheSameMailTwentyTimesAtOnce_PaysOutExactlyOnce()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        Guid mail = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 5)], 100));

        MailStatus[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => _mail.CollectAsync(player, mail))));

        results.Count(r => r == MailStatus.Ok).Should().Be(1, "one collect wins the race");
        results.Where(r => r != MailStatus.Ok).Should().OnlyContain(r => r == MailStatus.AlreadyCollected || r == MailStatus.Conflict);
        (await ParsnipsOfAsync(player)).Should().Be(5);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + 100, "the coins were paid once, not twenty times");
    }

    [Fact]
    public async Task Collect_ManyMailsAtOnce_EachPaysOutOnce()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        List<Guid> mails = [];
        for (int i = 0; i < 6; i++)
        {
            mails.Add(await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], MailAttachments.OfCoins(10)));
        }

        // Every mail is collected three times, all at once.
        MailStatus[] results = await Task.WhenAll(mails.SelectMany(m => Enumerable.Range(0, 3).Select(_ => Task.Run(() => _mail.CollectAsync(player, m)))));

        results.Count(r => r == MailStatus.Ok).Should().Be(6);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + 60);
    }

    [Fact]
    public async Task Collect_WhenTheInventoryIsFull_FailsWholeAndTheMailStaysCollectible()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        for (int i = 0; i < InventoryLimits.DefaultCapacity; i++)
        {
            _ = await _inventory.AddAsync(player, ItemCatalog.Radish, 99, "test.fill");
            if ((await _inventory.GetAsync(player)).Slots.Count == InventoryLimits.DefaultCapacity)
            {
                break;
            }
        }

        Guid mail = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 1)], 300));

        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.NoSpace);

        (await _transfers.GetBalanceAsync(player)).Should().Be(before, "the coins did not move either");
        (await _mail.ListAsync(player)).Single().Collected.Should().BeFalse();

        // Room is made; the same mail is collected after all.
        _ = await _inventory.RemoveAsync(player, ItemCatalog.Radish, 99, "test.free");
        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.Ok);
        (await ParsnipsOfAsync(player)).Should().Be(1);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + 300);
    }

    [Fact]
    public async Task Collect_MailWithoutAttachment_HasNothingToCollect()
    {
        Guid player = await CreatePlayerAsync();
        Guid mail = await _mail.SendAsync(player, MailRules.Codes.CoopInvite, ["Garden", "Lan"]);

        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.NothingToCollect);
    }

    [Fact]
    public async Task AnotherPlayersMail_CannotBeReadCollectedOrDeleted()
    {
        Guid owner = await CreatePlayerAsync();
        Guid thief = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(thief);
        Guid mail = await _mail.SendAsync(owner, MailRules.Codes.GoalReward, [], MailAttachments.OfCoins(1_000));

        (await _mail.ReadAsync(thief, mail)).Should().Be(MailStatus.NotFound);
        (await _mail.CollectAsync(thief, mail)).Should().Be(MailStatus.NotFound);
        (await _mail.DeleteAsync(thief, mail)).Should().Be(MailStatus.NotFound);

        (await _transfers.GetBalanceAsync(thief)).Should().Be(before);
        (await _mail.ListAsync(thief)).Should().BeEmpty();
        (await _mail.ListAsync(owner)).Single().Collected.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RefusesUncollectedAttachments_AllowsNoticesAndCollectedMail()
    {
        Guid player = await CreatePlayerAsync();
        Guid withCoins = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], MailAttachments.OfCoins(5));
        Guid withItems = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 1)]));
        Guid notice = await _mail.SendAsync(player, MailRules.Codes.MarketSold, ["1", "1", "1"]);

        (await _mail.DeleteAsync(player, withCoins)).Should().Be(MailStatus.HasAttachments);
        (await _mail.DeleteAsync(player, withItems)).Should().Be(MailStatus.HasAttachments);
        (await _mail.DeleteAsync(player, notice)).Should().Be(MailStatus.Ok);
        (await _mail.DeleteAsync(player, notice)).Should().Be(MailStatus.NotFound);

        (await _mail.CollectAsync(player, withCoins)).Should().Be(MailStatus.Ok);
        (await _mail.DeleteAsync(player, withCoins)).Should().Be(MailStatus.Ok);
        (await _mail.ListAsync(player)).Select(m => m.Id).Should().Equal(withItems);
        (await ItemLinesInDatabaseAsync(withCoins)).Should().Be(0);
    }

    [Fact]
    public async Task Expiry_AfterThirtyDays_TheMailIsInvisibleUncollectableAndPurged()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        Guid mail = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 2)], 400));
        _ = await _mail.SendAsync(player, MailRules.Codes.MarketSold, ["1", "1", "1"]);

        _clock.Advance(TimeSpan.FromDays(MailRules.ExpiryDays) - TimeSpan.FromMinutes(1));
        (await _mail.ListAsync(player)).Should().HaveCount(2, "one minute before the 30 days it is still there");

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await _mail.ListAsync(player)).Should().BeEmpty();
        (await _mail.CountUnreadAsync(player)).Should().Be(0);
        (await _mail.CollectAsync(player, mail)).Should().Be(MailStatus.Expired);
        (await _mail.ReadAsync(player, mail)).Should().Be(MailStatus.Expired);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before, "an expired attachment is not paid out");

        (await MailRowsInDatabaseAsync(player)).Should().Be(2, "invisible is not yet removed");
        (await _mail.PurgeExpiredAsync()).Should().BeGreaterThanOrEqualTo(2);
        (await MailRowsInDatabaseAsync(player)).Should().Be(0);
        (await ItemLinesInDatabaseAsync(mail)).Should().Be(0, "the attachment goes with its mail");
    }

    [Fact]
    public async Task Purge_KeepsMailThatHasNotExpired()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _mail.SendAsync(player, MailRules.Codes.MarketSold, ["1", "1", "1"]);
        _clock.Advance(TimeSpan.FromDays(10));

        _ = await _mail.PurgeExpiredAsync();

        (await _mail.ListAsync(player)).Should().ContainSingle();
    }

    [Fact]
    public async Task Send_PastTheStorageLimit_DropsTheOldestNoticesButNeverAnUncollectedAttachment()
    {
        Guid player = await CreatePlayerAsync();
        Guid keep = await _mail.SendAsync(player, MailRules.Codes.GoalReward, [], MailAttachments.OfCoins(9));
        for (int i = 0; i < 215; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _ = await _mail.SendAsync(player, MailRules.Codes.MarketSold, [i.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "1"]);
        }

        (await MailRowsInDatabaseAsync(player)).Should().BeLessThanOrEqualTo(200);
        MailView newest = (await _mail.ListAsync(player))[0];
        newest.Args[0].Should().Be("214", "the newest notice always fits");
        (await _mail.CollectAsync(player, keep)).Should().Be(MailStatus.Ok, "the oldest mail carried coins, so it was kept");
    }

    private async Task<int> ParsnipsOfAsync(Guid player)
    {
        InventoryView view = await _inventory.GetAsync(player);
        return view.Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity);
    }

    private async Task<int> MailRowsInDatabaseAsync(Guid player)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.MailMessages.CountAsync(m => m.RecipientId == player);
    }

    private async Task<int> ItemLinesInDatabaseAsync(Guid mail)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.MailAttachmentItems.CountAsync(i => i.MailId == mail);
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        Player player = TestNicknames.Name(new() { IsGuest = false, CreatedAt = s_start, LastActiveAt = s_start });
        context.Players.Add(player);
        _ = await context.SaveChangesAsync();
        return player.Id;
    }

    private sealed class RecordingNotifier : IPlayerNotifier
    {
        private readonly ConcurrentDictionary<Guid, List<int>> _unread = new();

        public List<int> UnreadOf(Guid player) => _unread.TryGetValue(player, out List<int>? list) ? [.. list] : [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            if (packet is MailUnread unread)
            {
                List<int> list = _unread.GetOrAdd(playerId, static _ => []);
                lock (list)
                {
                    list.Add(unread.Count);
                }
            }

            return ValueTask.FromResult(true);
        }
    }
}
