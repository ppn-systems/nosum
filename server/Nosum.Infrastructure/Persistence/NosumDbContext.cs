// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore;
using Nosum.Domain.Accounts;
using Nosum.Domain.Admin;
using Nosum.Domain.Blacksmith;
using Nosum.Domain.Buildings;
using Nosum.Domain.Community;
using Nosum.Domain.Coops;
using Nosum.Domain.Economy;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Looks;
using Nosum.Domain.Mail;
using Nosum.Domain.Market;
using Nosum.Domain.Mines;
using Nosum.Domain.Nodes;
using Nosum.Domain.Placeables;
using Nosum.Domain.Quests;
using Nosum.Domain.Requests;
using Nosum.Domain.Social;
using Nosum.Domain.Visits;
using Nosum.Domain.World;

namespace Nosum.Infrastructure.Persistence;

/// <summary>The Nosum database. Entity mapping lives in <c>Configurations/</c>, one class per entity.</summary>
public sealed class NosumDbContext : DbContext
{
    /// <summary>Initializes the context.</summary>
    /// <param name="options">Options built by <see cref="NosumDatabase.CreateOptions"/>.</param>
    public NosumDbContext(DbContextOptions<NosumDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the player accounts.</summary>
    public DbSet<Player> Players => this.Set<Player>();

    /// <summary>Gets the saved player states.</summary>
    public DbSet<PlayerState> PlayerStates => this.Set<PlayerState>();

    /// <summary>Gets the issued session tokens.</summary>
    public DbSet<PlayerSessionToken> PlayerSessionTokens => this.Set<PlayerSessionToken>();

    /// <summary>Gets the inventory headers.</summary>
    public DbSet<PlayerInventory> PlayerInventories => this.Set<PlayerInventory>();

    /// <summary>Gets the occupied inventory slots.</summary>
    public DbSet<InventorySlot> InventorySlots => this.Set<InventorySlot>();

    /// <summary>Gets the inventory audit trail.</summary>
    public DbSet<InventoryLogEntry> InventoryLog => this.Set<InventoryLogEntry>();

    /// <summary>Gets the tilled tiles of every map.</summary>
    public DbSet<FarmPlot> FarmPlots => this.Set<FarmPlot>();

    /// <summary>Gets the objects players placed (furniture in their farmhouse).</summary>
    public DbSet<PlacedObject> PlacedObjects => this.Set<PlacedObject>();

    /// <summary>Gets the item movements in and out of chests.</summary>
    public DbSet<ChestLogEntry> ChestLog => this.Set<ChestLogEntry>();

    /// <summary>Gets the smelting jobs of furnaces.</summary>
    public DbSet<FurnaceJob> FurnaceJobs => this.Set<FurnaceJob>();

    /// <summary>Gets the farm buildings (coops, barns) standing on farms and co-op land (docs/adr/ADR-028).</summary>
    public DbSet<FarmBuilding> FarmBuildings => this.Set<FarmBuilding>();

    /// <summary>Gets the animals living in farm buildings.</summary>
    public DbSet<FarmAnimal> FarmAnimals => this.Set<FarmAnimal>();

    /// <summary>Gets the resource nodes of every map instance.</summary>
    public DbSet<WorldNode> WorldNodes => this.Set<WorldNode>();

    /// <summary>Gets which map instances had their resource nodes seeded.</summary>
    public DbSet<NodeInstanceState> NodeInstances => this.Set<NodeInstanceState>();

    /// <summary>Gets the items lying on the ground of every map instance.</summary>
    public DbSet<GroundItem> GroundItems => this.Set<GroundItem>();

    /// <summary>Gets the players' quests.</summary>
    public DbSet<PlayerQuest> PlayerQuests => this.Set<PlayerQuest>();

    /// <summary>Gets the coin balances.</summary>
    public DbSet<PlayerWallet> PlayerWallets => this.Set<PlayerWallet>();

    /// <summary>Gets the coin audit trail.</summary>
    public DbSet<CoinLogEntry> CoinLog => this.Set<CoinLogEntry>();

    /// <summary>Gets the players' looks.</summary>
    public DbSet<PlayerAppearance> PlayerAppearances => this.Set<PlayerAppearance>();

    /// <summary>Gets the consignment market listings.</summary>
    public DbSet<MarketListing> MarketListings => this.Set<MarketListing>();

    /// <summary>Gets the sales of the consignment market, for the price history.</summary>
    public DbSet<MarketSale> MarketSales => this.Set<MarketSale>();

    /// <summary>Gets the community goals of every season (docs/adr/ADR-030).</summary>
    public DbSet<CommunityGoalProgress> CommunityGoals => this.Set<CommunityGoalProgress>();

    /// <summary>Gets what each player delivered to each community goal.</summary>
    public DbSet<CommunityContribution> CommunityContributions => this.Set<CommunityContribution>();

    /// <summary>Gets the unlock flags set by completed goals.</summary>
    public DbSet<CommunityUnlockRecord> CommunityUnlocks => this.Set<CommunityUnlockRecord>();

    /// <summary>Gets the requests of each game day on the Town board (docs/adr/ADR-036).</summary>
    public DbSet<DailyRequest> DailyRequests => this.Set<DailyRequest>();

    /// <summary>Gets which player filled which request of which day.</summary>
    public DbSet<RequestFill> RequestFills => this.Set<RequestFill>();

    /// <summary>Gets the co-ops.</summary>
    public DbSet<Coop> Coops => this.Set<Coop>();

    /// <summary>Gets the co-op memberships.</summary>
    public DbSet<CoopMember> CoopMembers => this.Set<CoopMember>();

    /// <summary>Gets the open co-op invitations.</summary>
    public DbSet<CoopInvite> CoopInvites => this.Set<CoopInvite>();

    /// <summary>Gets the warehouse stock of every co-op.</summary>
    public DbSet<CoopStockItem> CoopStock => this.Set<CoopStockItem>();

    /// <summary>Gets the co-op warehouse audit trail.</summary>
    public DbSet<CoopLogEntry> CoopLog => this.Set<CoopLogEntry>();

    /// <summary>Gets the allow-lists: which players may visit which owner's farm.</summary>
    public DbSet<VisitGrant> VisitGrants => this.Set<VisitGrant>();

    /// <summary>Gets the owners' visiting settings.</summary>
    public DbSet<VisitSettings> VisitSettings => this.Set<VisitSettings>();

    /// <summary>Gets how deep each player has been in the mine.</summary>
    public DbSet<PlayerMine> PlayerMines => this.Set<PlayerMine>();

    /// <summary>Gets the tools the blacksmith is upgrading.</summary>
    public DbSet<ToolUpgrade> ToolUpgrades => this.Set<ToolUpgrade>();
    /// <summary>Gets the block lists (docs/adr/ADR-029 D3).</summary>
    public DbSet<BlockedPlayer> BlockedPlayers => this.Set<BlockedPlayer>();

    /// <summary>Gets the chat reports.</summary>
    public DbSet<ChatReport> ChatReports => this.Set<ChatReport>();

    /// <summary>Gets the messages stored with the chat reports.</summary>
    public DbSet<ChatReportLine> ChatReportLines => this.Set<ChatReportLine>();

    /// <summary>Gets the friendships, one row per side (docs/adr/ADR-029 D4).</summary>
    public DbSet<Friendship> Friendships => this.Set<Friendship>();

    /// <summary>Gets the open friend requests.</summary>
    public DbSet<FriendRequest> FriendRequests => this.Set<FriendRequest>();

    /// <summary>Gets the mails of every mailbox (docs/adr/ADR-029 D5).</summary>
    public DbSet<MailMessage> MailMessages => this.Set<MailMessage>();

    /// <summary>Gets the items attached to mails.</summary>
    public DbSet<MailAttachmentItem> MailAttachmentItems => this.Set<MailAttachmentItem>();

    /// <summary>Gets the audit trail of admin actions (docs/adr/ADR-031 D7).</summary>
    public DbSet<AdminAuditEntry> AdminAudit => this.Set<AdminAuditEntry>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(NosumDbContext).Assembly);
    }
}
