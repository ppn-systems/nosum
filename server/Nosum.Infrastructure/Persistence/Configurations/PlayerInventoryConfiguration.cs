// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Inventory;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerInventoryConfiguration : IEntityTypeConfiguration<PlayerInventory>
{
    public void Configure(EntityTypeBuilder<PlayerInventory> builder)
    {
        _ = builder.ToTable("player_inventories");
        _ = builder.HasKey(i => i.PlayerId);
        _ = builder.Property(i => i.PlayerId).ValueGeneratedNever();

        // The key is an inventory owner id: a player's account id, or a container's derived id (docs/adr/ADR-024 D4, ContainerIds), so there is no foreign key to players.
    }
}
