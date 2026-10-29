// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Market;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class MarketSaleConfiguration : IEntityTypeConfiguration<MarketSale>
{
    public void Configure(EntityTypeBuilder<MarketSale> builder)
    {
        _ = builder.ToTable("market_sales");
        _ = builder.HasKey(s => s.Id);
        _ = builder.Property(s => s.Id).ValueGeneratedNever();
        _ = builder.HasIndex(s => new { s.ItemId, s.Day });
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_market_sales_amounts", "quantity > 0 AND price > 0 AND unit_price > 0"));
    }
}
