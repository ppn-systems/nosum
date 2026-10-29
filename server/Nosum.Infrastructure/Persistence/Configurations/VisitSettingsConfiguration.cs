// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Visits;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class VisitSettingsConfiguration : IEntityTypeConfiguration<VisitSettings>
{
    public void Configure(EntityTypeBuilder<VisitSettings> builder)
    {
        _ = builder.ToTable("visit_settings");
        _ = builder.HasKey(s => s.OwnerId);
        _ = builder.Property(s => s.OwnerId).ValueGeneratedNever();
        _ = builder.Property(s => s.FriendsMayVisit).HasDefaultValue(true).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
