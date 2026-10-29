// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Visits;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Visits;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Visits;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The farm guestbook (docs/adr/ADR-066) against a real PostgreSQL database: a visitor's waters count into one row per day, a read shows the newest days and
/// clears the new marks, every new entry is counted even when only the newest are shown, two first waterings racing both count, and one owner's log never shows another's.
/// </summary>
public sealed class GuestbookServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public GuestbookServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task RecordWatering_TwiceOnOneDay_CountsOneRowWithTwoWaters_AndMarksItNew()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        GuestbookService guestbook = Create(world);

        await guestbook.RecordWateringAsync(owner, visitor, 5);
        await guestbook.RecordWateringAsync(owner, visitor, 5);

        await using NosumDbContext db = world.Db();
        GuestbookEntry entry = await db.GuestbookEntries.SingleAsync(e => e.OwnerId == owner);
        (entry.VisitorId, entry.DayNumber, entry.Waters, entry.Unread).Should().Be((visitor, 5, 2, true));
    }

    [Fact]
    public async Task Read_ReturnsTheNewestDaysFirst_CountsTheNewOnes_AndClearsThem()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        GuestbookService guestbook = Create(world);
        await guestbook.RecordWateringAsync(owner, visitor, 4);
        await guestbook.RecordWateringAsync(owner, visitor, 5);
        await guestbook.RecordWateringAsync(owner, visitor, 5);

        GuestbookView first = await guestbook.ReadAsync(owner);

        first.UnreadCount.Should().Be(2);
        first.Rows.Select(r => (r.DayNumber, r.Waters, r.Unread)).Should().Equal((5, 2, true), (4, 1, true));
        first.Rows[0].Nickname.Should().Be(await new NicknameDirectory(world.Units).GetAsync(visitor));

        GuestbookView second = await guestbook.ReadAsync(owner);

        second.UnreadCount.Should().Be(0);
        second.Rows.Should().HaveCount(2).And.OnlyContain(r => !r.Unread);
    }

    [Fact]
    public async Task Read_ShowsOnlyTheNewestRows_ButCountsEveryNewEntry()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        GuestbookService guestbook = Create(world);
        for (int day = 1; day <= GuestbookRules.MaxRows + 1; day++)
        {
            await guestbook.RecordWateringAsync(owner, visitor, day);
        }

        GuestbookView view = await guestbook.ReadAsync(owner);

        view.UnreadCount.Should().Be(GuestbookRules.MaxRows + 1);
        view.Rows.Should().HaveCount(GuestbookRules.MaxRows);
        view.Rows[0].DayNumber.Should().Be(GuestbookRules.MaxRows + 1);
        view.Rows[^1].DayNumber.Should().Be(2);
    }

    [Fact]
    public async Task RecordWatering_ManyWateringsRacingOnOneNewDay_CountsEveryOne()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        GuestbookService guestbook = Create(world);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => guestbook.RecordWateringAsync(owner, visitor, 9)));

        await using NosumDbContext db = world.Db();
        (await db.GuestbookEntries.SingleAsync(e => e.OwnerId == owner)).Waters.Should().Be(6);
    }

    [Fact]
    public async Task Read_OfOneOwner_NeverShowsAnotherOwnersEntries()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid other = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        GuestbookService guestbook = Create(world);
        await guestbook.RecordWateringAsync(other, visitor, 3);

        GuestbookView view = await guestbook.ReadAsync(owner);

        view.UnreadCount.Should().Be(0);
        view.Rows.Should().BeEmpty();
        (await guestbook.ReadAsync(other)).UnreadCount.Should().Be(1, "the other owner's entry was not read by the first owner");
    }

    private static GuestbookService Create(PlacementHarness world) => new(world.Units, new NicknameDirectory(world.Units), world.Clock);
}
