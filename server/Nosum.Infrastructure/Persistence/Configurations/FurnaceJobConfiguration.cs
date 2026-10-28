// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Farm;
using Nosum.Domain.Placeables;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class FurnaceJobConfiguration : IEntityTypeConfiguration<FurnaceJob>
{
    public void Configure(EntityTypeBuilder<FurnaceJob> builder)
    {
        _ = builder.ToTable("furnace_jobs");
        _ = builder.HasKey(j => j.ObjectId);
        _ = builder.Property(j => j.ObjectId).ValueGeneratedNever();
        _ = builder.HasOne<PlacedObject>().WithMany().HasForeignKey(j => j.ObjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
