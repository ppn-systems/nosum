// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Buildings;

namespace Nosum.Infrastructure.Persistence.Configurations;

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
