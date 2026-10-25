// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.World;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class GroundItemConfiguration : IEntityTypeConfiguration<GroundItem>
{
    public void Configure(EntityTypeBuilder<GroundItem> builder)
    {
        _ = builder.ToTable("ground_items");
        _ = builder.HasKey(g => g.Id);
        _ = builder.Property(g => g.Id).ValueGeneratedOnAdd();
        _ = builder.Property(g => g.MapId).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(g => new { g.MapId, g.OwnerId });
    }
}
