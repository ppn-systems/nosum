// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Visits;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class VisitGrantConfiguration : IEntityTypeConfiguration<VisitGrant>
{
    public void Configure(EntityTypeBuilder<VisitGrant> builder)
    {
        _ = builder.ToTable("visit_grants");
        _ = builder.HasKey(g => new { g.OwnerId, g.VisitorId });
        _ = builder.HasIndex(g => g.VisitorId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(g => g.VisitorId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_visit_grants_not_self", "owner_id <> visitor_id"));
    }
}

internal sealed class VisitSettingsConfiguration : IEntityTypeConfiguration<VisitSettings>
{
    public void Configure(EntityTypeBuilder<VisitSettings> builder)
    {
        _ = builder.ToTable("visit_settings");
        _ = builder.HasKey(s => s.OwnerId);
        _ = builder.Property(s => s.OwnerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
