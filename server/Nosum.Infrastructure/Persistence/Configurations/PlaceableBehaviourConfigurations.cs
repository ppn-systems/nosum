// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Placeables;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class ChestLogEntryConfiguration : IEntityTypeConfiguration<ChestLogEntry>
{
    public void Configure(EntityTypeBuilder<ChestLogEntry> builder)
    {
        _ = builder.ToTable("chest_log");
        _ = builder.HasKey(e => e.Id);
        _ = builder.Property(e => e.Id).ValueGeneratedNever();
        _ = builder.Property(e => e.Reason).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(e => new { e.ObjectId, e.OccurredAt });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
