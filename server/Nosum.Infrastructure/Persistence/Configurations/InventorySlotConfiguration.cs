// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Inventory;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class InventorySlotConfiguration : IEntityTypeConfiguration<InventorySlot>
{
    public void Configure(EntityTypeBuilder<InventorySlot> builder)
    {
        _ = builder.ToTable("inventory_slots");
        _ = builder.HasKey(s => new { s.PlayerId, s.Slot });
        _ = builder.HasOne<PlayerInventory>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_inventory_slots_quantity", "quantity > 0"));
    }
}
