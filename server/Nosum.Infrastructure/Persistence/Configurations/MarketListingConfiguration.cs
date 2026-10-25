// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Market;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class MarketListingConfiguration : IEntityTypeConfiguration<MarketListing>
{
    public void Configure(EntityTypeBuilder<MarketListing> builder)
    {
        _ = builder.ToTable("market_listings");
        _ = builder.HasKey(l => l.Id);
        _ = builder.Property(l => l.Id).ValueGeneratedNever();
        _ = builder.Property(l => l.Status).IsConcurrencyToken();
        _ = builder.HasIndex(l => new { l.Status, l.ItemId, l.CreatedAt });
        _ = builder.HasIndex(l => new { l.SellerId, l.Status });
        _ = builder.HasIndex(l => new { l.Status, l.ExpiresAt });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(l => l.SellerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_market_listings_amounts", "quantity > 0 AND price > 0"));
    }
}
