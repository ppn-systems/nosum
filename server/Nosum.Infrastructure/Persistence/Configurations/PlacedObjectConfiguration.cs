// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Farm;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlacedObjectConfiguration : IEntityTypeConfiguration<PlacedObject>
{
    public void Configure(EntityTypeBuilder<PlacedObject> builder)
    {
        _ = builder.ToTable("placed_objects");
        _ = builder.HasKey(p => p.Id);
        _ = builder.Property(p => p.Id).ValueGeneratedOnAdd();
        _ = builder.Property(p => p.MapId).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(p => new { p.MapId, p.OwnerId });
    }
}
