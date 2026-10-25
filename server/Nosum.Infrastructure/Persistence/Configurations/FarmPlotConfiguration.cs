// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Farm;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class FarmPlotConfiguration : IEntityTypeConfiguration<FarmPlot>
{
    public void Configure(EntityTypeBuilder<FarmPlot> builder)
    {
        _ = builder.ToTable("farm_plots");
        _ = builder.HasKey(p => new { p.MapId, p.X, p.Y });
        _ = builder.Property(p => p.MapId).HasMaxLength(64).IsRequired();
    }
}
