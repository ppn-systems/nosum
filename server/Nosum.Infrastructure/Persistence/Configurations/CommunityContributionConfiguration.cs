// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Community;

namespace Nosum.Infrastructure.Persistence.Configurations;

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
