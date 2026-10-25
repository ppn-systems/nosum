// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Runtime.Groups;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Market;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The first producers of system mail (docs/adr/ADR-029 D5): a market sale tells the seller, and a failing mail never undoes the sale.</summary>
public sealed class MailProducersTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly NicknameDirectory _nicknames;
    private readonly MailService _mail;

    public MailProducersTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _nicknames = new NicknameDirectory(_units);
        _mail = new MailService(_units, _transfers, new SilentNotifier(), _clock);
    }

    [Fact]
    public async Task Buy_TellsTheSellerByMail_WithTheItemQuantityAndTheCoinsPaidOut()
    {
        MarketService market = this.NewMarket(_mail);
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(market, quantity: 10, price: 200);

        (await market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.Ok);

        MailView notice = (await _mail.ListAsync(seller)).Should().ContainSingle().Subject;
        notice.Code.Should().Be(MailRules.Codes.MarketSold);
        notice.Args.Should().Equal(ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "10", (200 - CoinRules.MarketFee(200)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        notice.Coins.Should().Be(0, "the coins were paid straight into the wallet as before; the mail is only the notice");
        (await _mail.ListAsync(buyer)).Should().BeEmpty();
    }

    [Fact]
    public async Task Buy_ThatFails_SendsNoMail()
    {
        MarketService market = this.NewMarket(_mail);
        (Guid seller, Guid _, Guid listing) = await ListedAsync(market, quantity: 1, price: 100);
        Guid broke = await CreatePlayerAsync();
        _ = await _transfers.ExecuteAsync([new(Nosum.Abstractions.Economy.TransferParty.Player(broke), 0, 0, -CoinRules.StartingCoins)], "test.drain");

        (await market.BuyAsync(broke, listing)).Should().Be(MarketStatus.NotEnoughCoins);

        (await _mail.ListAsync(seller)).Should().BeEmpty();
    }

    [Fact]
    public async Task Buy_WhenTheMailServiceThrows_StillCompletesTheSale()
    {
        MarketService market = this.NewMarket(new ThrowingMail());
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(market, quantity: 4, price: 80);

        (await market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.Ok, "the notice is best effort");

        (await _transfers.GetBalanceAsync(seller)).Should().Be(CoinRules.StartingCoins + 80 - CoinRules.MarketFee(80));
    }

    [Fact]
    public async Task Buy_WithoutAMailService_BehavesAsBefore()
    {
        MarketService market = this.NewMarket(null);
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(market, quantity: 2, price: 60);

        (await market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.Ok);

        (await _mail.ListAsync(seller)).Should().BeEmpty();
    }

    private MarketService NewMarket(IMailService? mail)
        => new(_transfers, _inventory, _units, MarketCounter.Presence(), MarketCounter.Maps(), new CoinSink(), _nicknames, _clock, mail: mail);

    private async Task<(Guid Seller, Guid Buyer, Guid Listing)> ListedAsync(MarketService market, int quantity, long price)
    {
        Guid seller = await CreatePlayerAsync();
        Guid buyer = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, quantity, "test.setup");
        int slot = (await _inventory.GetAsync(seller)).Slots.First(s => s.ItemId == ItemCatalog.Parsnip).Slot;
        (await market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, quantity, price)).Should().Be(MarketStatus.Ok);
        return (seller, buyer, (await market.MineAsync(seller)).Listings.Single().ListingId);
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        Player player = TestNicknames.Name(new() { IsGuest = false, CreatedAt = s_start, LastActiveAt = s_start });
        context.Players.Add(player);
        _ = await context.SaveChangesAsync();
        return player.Id;
    }

    private sealed class SilentNotifier : IPlayerNotifier
    {
        public ValueTask<bool> TrySendAsync(Guid playerId, Nalix.Abstractions.Networking.Packets.IPacket packet, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class ThrowingMail : IMailService
    {
        public Task<Guid> SendAsync(Guid recipientId, string code, IReadOnlyList<string> args, MailAttachments? attachments = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("mail is down");

        public Task<IReadOnlyList<MailView>> ListAsync(Guid playerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountUnreadAsync(Guid playerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Nosum.Contracts.Mail.MailStatus> ReadAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Nosum.Contracts.Mail.MailStatus> CollectAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Nosum.Contracts.Mail.MailStatus> DeleteAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
