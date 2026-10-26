// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Mail;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Mail;
using Nosum.Infrastructure.Caching;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Persistence;
using Nosum.IntegrationTests.Server.DbBudget;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The write-behind rows of docs/adr/ADR-035 D7: the choice is visible at once, the row is written within the window, at a flush or at shutdown, and a crash loses nothing but the choice.</summary>
public sealed class WriteBehindTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly DbCommandCounter _counter = new();
    private readonly EfUnitOfWorkFactory _units;
    private readonly WriteBehind _waiting;
    private readonly InventoryService _inventory;
    private readonly MailService _mail;
    private readonly List<Exception> _failures = [];

    public WriteBehindTests(PostgresFixture database)
    {
        _database = database;
        DbContextOptions<NosumDbContext> options = new DbContextOptionsBuilder<NosumDbContext>().UseNpgsql(database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(_counter).Options;
        _units = new EfUnitOfWorkFactory(options);
        _waiting = new WriteBehind(_units, _clock);
        _waiting.Failed += _failures.Add;
        _inventory = new InventoryService(_units, _clock, null, _waiting);
        _mail = new MailService(_units, new TransferService(_inventory, _clock), new NoNotifier(), _clock, _waiting);
    }

    public void Dispose() => _waiting.Dispose();

    [Fact]
    public async Task SelectSlotAsync_IsSeenAtOnceAndWrittenWhenFlushed()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);

        int mark = _counter.Mark;
        (InventoryStatus status, int selected) = await _inventory.SelectSlotAsync(player, 4);

        (status, selected).Should().Be((InventoryStatus.Ok, 4));
        _counter.Since(mark).Commands.Should().Be(0, "nothing is written until the window has passed");
        (await _inventory.GetAsync(player)).SelectedSlot.Should().Be(4);
        (await SelectedInRowsAsync(player)).Should().Be(0);
        _waiting.Pending.Should().Be(1);

        await _waiting.FlushAsync(player);

        (await SelectedInRowsAsync(player)).Should().Be(4);
        _waiting.Pending.Should().Be(0);
    }

    [Fact]
    public async Task SelectSlotAsync_ManyChangesInOneWindow_CostOneWrite()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);

        int mark = _counter.Mark;
        foreach (int slot in new[] { 1, 2, 3, 2, 5, 6 })
        {
            _ = await _inventory.SelectSlotAsync(player, slot);
        }

        await _waiting.FlushAsync(player);

        DbCounts spent = _counter.Since(mark);
        (spent.Reads, spent.Writes).Should().Be((0, 1));
        (await SelectedInRowsAsync(player)).Should().Be(6);
    }

    [Fact]
    public async Task SelectSlotAsync_ADroppedEntryReloadsTheChoiceThatIsStillWaiting()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);
        _ = await _inventory.SelectSlotAsync(player, 7);

        _inventory.Forget(player); // a failed save elsewhere dropped the entry

        (await _inventory.GetAsync(player)).SelectedSlot.Should().Be(7, "the row is older than the choice that waits for its write");
        await _waiting.FlushAsync(player);
        (await SelectedInRowsAsync(player)).Should().Be(7);
    }

    [Fact]
    public async Task SelectSlotAsync_OutsideTheHotbar_IsRefusedAndStagesNothing()
    {
        Guid player = await CreatePlayerAsync();

        (InventoryStatus status, int selected) = await _inventory.SelectSlotAsync(player, 9);

        (status, selected).Should().Be((InventoryStatus.InvalidSlot, 0));
        _waiting.Pending.Should().Be(0);
    }

    [Fact]
    public async Task FlushDueAsync_WritesOnlyWhatsWindowHasPassed()
    {
        Guid early = await CreatePlayerAsync();
        Guid late = await CreatePlayerAsync();
        _ = await _inventory.SelectSlotAsync(early, 3);
        _clock.Advance(TimeSpan.FromSeconds(3));
        _ = await _inventory.SelectSlotAsync(late, 5);
        _clock.Advance(TimeSpan.FromSeconds(3));

        await _waiting.FlushDueAsync();

        (await SelectedInRowsAsync(early)).Should().Be(3);
        (await SelectedInRowsAsync(late)).Should().Be(0, "its five seconds have not passed");
        _clock.Advance(TimeSpan.FromSeconds(3));
        await _waiting.FlushDueAsync();
        (await SelectedInRowsAsync(late)).Should().Be(5);
    }

    [Fact]
    public async Task FlushAllAsync_AtShutdown_WritesEveryPlayersRows()
    {
        Guid a = await CreatePlayerAsync();
        Guid b = await CreatePlayerAsync();
        Guid c = await CreatePlayerAsync();
        Guid mail = await _mail.SendAsync(c, "test.one", []);
        _ = await _inventory.SelectSlotAsync(a, 1);
        _ = await _inventory.SelectSlotAsync(b, 2);
        _ = await _mail.ReadAsync(c, mail);
        _waiting.Pending.Should().Be(3);

        await _waiting.FlushAllAsync();

        (await SelectedInRowsAsync(a), await SelectedInRowsAsync(b)).Should().Be((1, 2));
        (await ReadInRowsAsync(mail)).Should().NotBeNull();
        _waiting.Pending.Should().Be(0);
        _failures.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_OnePlayersChoiceAndMailRead_AreOneTransaction()
    {
        Guid player = await CreatePlayerAsync();
        Guid mail = await _mail.SendAsync(player, "test.one", []);
        _ = await _inventory.GetAsync(player);
        _ = await _inventory.SelectSlotAsync(player, 5);
        _ = await _mail.ReadAsync(player, mail);

        int mark = _counter.Mark;
        await _waiting.FlushAsync(player);

        DbCounts spent = _counter.Since(mark);
        (spent.Writes, spent.SaveChanges).Should().Be((1, 1), "EF batches both rows into one command of one save");
    }

    [Fact]
    public async Task ReadAsync_IsSeenByTheListAndTheUnreadCountBeforeItIsWritten()
    {
        Guid player = await CreatePlayerAsync();
        Guid first = await _mail.SendAsync(player, "test.one", []);
        _ = await _mail.SendAsync(player, "test.two", []);

        MailStatus status = await _mail.ReadAsync(player, first);

        status.Should().Be(MailStatus.Ok);
        (await _mail.CountUnreadAsync(player)).Should().Be(1);
        (await _mail.ListAsync(player)).Single(m => m.Id == first).Read.Should().BeTrue();
        (await ReadInRowsAsync(first)).Should().BeNull("not written yet");
        (await _mail.ReadAsync(player, first)).Should().Be(MailStatus.Ok, "reading twice is harmless");

        await _waiting.FlushAsync(player);

        (await ReadInRowsAsync(first)).Should().NotBeNull();
        (await _mail.CountUnreadAsync(player)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_ADeletedMailWaitingForItsReadFlag_IsForgotten()
    {
        Guid player = await CreatePlayerAsync();
        Guid mail = await _mail.SendAsync(player, "test.one", []);
        _ = await _mail.ReadAsync(player, mail);

        (await _mail.DeleteAsync(player, mail)).Should().Be(MailStatus.Ok);
        await _waiting.FlushAllAsync();

        _waiting.Pending.Should().Be(0);
        _failures.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_AMailRemovedBehindTheQueue_DoesNotCostTheOtherRows()
    {
        Guid player = await CreatePlayerAsync();
        Guid gone = await _mail.SendAsync(player, "test.one", []);
        _ = await _inventory.GetAsync(player);
        _ = await _inventory.SelectSlotAsync(player, 6);
        _ = await _mail.ReadAsync(player, gone);
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.MailMessages.Where(m => m.Id == gone).ExecuteDeleteAsync();
        }

        await _waiting.FlushAsync(player);

        (await SelectedInRowsAsync(player)).Should().Be(6, "the hotbar choice was still written");
        _failures.Should().ContainSingle("the mail row could not be written and was dropped");
    }

    private async Task<int> SelectedInRowsAsync(Guid player)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.PlayerInventories.Where(i => i.PlayerId == player).Select(i => i.SelectedSlot).SingleAsync();
    }

    private async Task<DateTimeOffset?> ReadInRowsAsync(Guid mail)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.MailMessages.Where(m => m.Id == mail).Select(m => m.ReadAt).SingleAsync();
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    private sealed class NoNotifier : IPlayerNotifier
    {
        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
