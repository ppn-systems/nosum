// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetCodeConfiguration : IEntityTypeConfiguration<PasswordResetCode>
{
    public void Configure(EntityTypeBuilder<PasswordResetCode> builder)
    {
        _ = builder.ToTable("password_reset_codes");
        _ = builder.HasKey(c => c.Id);
        _ = builder.Property(c => c.Id).ValueGeneratedNever();
        _ = builder.Property(c => c.CodeHash).HasMaxLength(256).IsRequired();
        _ = builder.HasIndex(c => new { c.PlayerId, c.CreatedAt });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(c => c.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
