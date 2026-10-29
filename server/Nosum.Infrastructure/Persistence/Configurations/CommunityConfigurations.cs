// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Community;

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
