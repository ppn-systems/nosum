// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopLogEntryConfiguration : IEntityTypeConfiguration<CoopLogEntry>
{
    public void Configure(EntityTypeBuilder<CoopLogEntry> builder)
    {
        _ = builder.ToTable("coop_log");
        _ = builder.HasKey(e => e.Id);
        _ = builder.Property(e => e.Id).ValueGeneratedNever();
        _ = builder.Property(e => e.Reason).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(e => new { e.CoopId, e.OccurredAt });
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(e => e.CoopId).OnDelete(DeleteBehavior.Cascade);
    }
}
