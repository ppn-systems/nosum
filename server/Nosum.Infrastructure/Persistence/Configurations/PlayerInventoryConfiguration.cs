// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Inventory;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerInventoryConfiguration : IEntityTypeConfiguration<PlayerInventory>
{
    public void Configure(EntityTypeBuilder<PlayerInventory> builder)
    {
        _ = builder.ToTable("player_inventories");
        _ = builder.HasKey(i => i.PlayerId);
        _ = builder.Property(i => i.PlayerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(i => i.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
