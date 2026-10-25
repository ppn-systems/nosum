// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Economy;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Looks;
using Nosum.Domain.Market;
using Nosum.Domain.Nodes;
using Nosum.Domain.Quests;
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

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(NosumDbContext).Assembly);
    }
}
