// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Buildings;
using Nosum.Domain.Farm;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class FarmBuildingConfiguration : IEntityTypeConfiguration<FarmBuilding>
{
    public void Configure(EntityTypeBuilder<FarmBuilding> builder)
    {
        _ = builder.ToTable("farm_buildings");
        _ = builder.HasKey(b => b.Id);
        _ = builder.Property(b => b.Id).ValueGeneratedNever();
        _ = builder.Property(b => b.MapId).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(b => b.ObjectId).IsUnique();
        _ = builder.HasIndex(b => new { b.MapId, b.OwnerId });
        _ = builder.HasOne(b => b.Placed).WithOne().HasForeignKey<FarmBuilding>(b => b.ObjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FarmAnimalConfiguration : IEntityTypeConfiguration<FarmAnimal>
{
    public void Configure(EntityTypeBuilder<FarmAnimal> builder)
    {
        _ = builder.ToTable("farm_animals");
        _ = builder.HasKey(a => a.Id);
        _ = builder.Property(a => a.Id).ValueGeneratedNever();
        _ = builder.Property(a => a.Name).HasMaxLength(32).IsRequired();
        _ = builder.HasIndex(a => a.BuildingId);
        _ = builder.HasOne<FarmBuilding>().WithMany().HasForeignKey(a => a.BuildingId).OnDelete(DeleteBehavior.Cascade);
    }
}
