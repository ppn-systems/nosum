// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopStockItemConfiguration : IEntityTypeConfiguration<CoopStockItem>
{
    public void Configure(EntityTypeBuilder<CoopStockItem> builder)
    {
        _ = builder.ToTable("coop_stock");
        _ = builder.HasKey(s => new { s.CoopId, s.ItemId });
        _ = builder.Property(s => s.Quantity).IsConcurrencyToken();
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(s => s.CoopId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_coop_stock_quantity", "quantity > 0"));
    }
}
