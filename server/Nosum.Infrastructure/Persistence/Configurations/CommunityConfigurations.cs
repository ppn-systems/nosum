// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Community;
using Nosum.Domain.Market;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CommunityGoalProgressConfiguration : IEntityTypeConfiguration<CommunityGoalProgress>
{
    public void Configure(EntityTypeBuilder<CommunityGoalProgress> builder)
    {
        _ = builder.ToTable("community_goals");
        _ = builder.HasKey(g => g.Id);
        _ = builder.Property(g => g.Id).ValueGeneratedNever();
        _ = builder.Property(g => g.Total).IsConcurrencyToken();
        _ = builder.Property(g => g.Status).IsConcurrencyToken();
        _ = builder.HasIndex(g => new { g.GoalId, g.SeasonNumber }).IsUnique();
        _ = builder.HasIndex(g => g.Status);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_community_goals_total", "total >= 0"));
    }
}

internal sealed class CommunityContributionConfiguration : IEntityTypeConfiguration<CommunityContribution>
{
    public void Configure(EntityTypeBuilder<CommunityContribution> builder)
    {
        _ = builder.ToTable("community_contributions");
        _ = builder.HasKey(c => c.Id);
        _ = builder.Property(c => c.Id).ValueGeneratedNever();
        _ = builder.Property(c => c.PaidCoins).IsConcurrencyToken();
        _ = builder.HasIndex(c => new { c.ProgressId, c.PlayerId }).IsUnique();
        _ = builder.HasIndex(c => c.PlayerId);
        _ = builder.HasOne<CommunityGoalProgress>().WithMany().HasForeignKey(c => c.ProgressId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(c => c.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_community_contributions_delivered", "delivered > 0"));
    }
}

internal sealed class CommunityUnlockRecordConfiguration : IEntityTypeConfiguration<CommunityUnlockRecord>
{
    public void Configure(EntityTypeBuilder<CommunityUnlockRecord> builder)
    {
        _ = builder.ToTable("community_unlocks");
        _ = builder.HasKey(u => u.Key);
        _ = builder.Property(u => u.Key).HasMaxLength(64).ValueGeneratedNever();
    }
}

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
