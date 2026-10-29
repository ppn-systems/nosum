// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        _ = builder.ToTable("external_logins");
        _ = builder.HasKey(l => l.Id);
        _ = builder.Property(l => l.Id).ValueGeneratedNever();
        // Stored as smallint (the enum value): the numbers are append-only (docs/adr/ADR-069).
        _ = builder.Property(l => l.Provider).HasConversion<short>();
        _ = builder.Property(l => l.Subject).HasMaxLength(ExternalLogin.MaxSubjectLength).IsRequired();
        // The identity is the key: one account per provider subject, and none without a player.
        _ = builder.HasIndex(l => new { l.Provider, l.Subject }).IsUnique();
        _ = builder.HasIndex(l => l.PlayerId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(l => l.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
