// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Admin;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class AdminAuditEntryConfiguration : IEntityTypeConfiguration<AdminAuditEntry>
{
    public void Configure(EntityTypeBuilder<AdminAuditEntry> builder)
    {
        _ = builder.ToTable("admin_audit");
        _ = builder.HasKey(e => e.Id);
        _ = builder.Property(e => e.Id).ValueGeneratedNever();
        _ = builder.Property(e => e.Detail).HasMaxLength(200).IsRequired();
        _ = builder.HasIndex(e => e.CreatedAt);
        _ = builder.HasIndex(e => e.TargetId);
    }
}
