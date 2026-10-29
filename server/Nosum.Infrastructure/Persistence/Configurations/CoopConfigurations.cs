// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopConfiguration : IEntityTypeConfiguration<Coop>
{
    public void Configure(EntityTypeBuilder<Coop> builder)
    {
        _ = builder.ToTable("coops");
        _ = builder.HasKey(c => c.Id);
        _ = builder.Property(c => c.Id).ValueGeneratedNever();
        _ = builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(c => c.WarehouseLevel).HasDefaultValue(0);
        _ = builder.Property(c => c.LandLevel).HasDefaultValue(0);

        // The shared objective ledger (docs/adr/ADR-067 D2): one small JSON column, read with the co-op row instead of a table of its own.
        _ = builder.Property(c => c.Objectives)
            .HasColumnName("objectives")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(
                ledger => JsonSerializer.Serialize(ledger, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<Dictionary<int, CoopObjectiveProgress>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<int, CoopObjectiveProgress>());
    }
}
