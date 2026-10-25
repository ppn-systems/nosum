// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Contracts.Account;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        _ = builder.ToTable("players");
        _ = builder.HasKey(p => p.Id);
        _ = builder.Property(p => p.Id).ValueGeneratedNever();
        // Guests have no e-mail; PostgreSQL unique indexes allow many NULLs.
        _ = builder.Property(p => p.Email).HasMaxLength(AccountLimits.EmailMaxLength);
        _ = builder.Property(p => p.NormalizedEmail).HasMaxLength(AccountLimits.EmailMaxLength);
        _ = builder.HasIndex(p => p.NormalizedEmail).IsUnique();
        _ = builder.Property(p => p.Nickname).HasMaxLength(32).IsRequired();
        _ = builder.Property(p => p.NormalizedNickname).HasMaxLength(32).IsRequired();
        _ = builder.HasIndex(p => p.NormalizedNickname).IsUnique();
        _ = builder.Property(p => p.PasswordHash).HasMaxLength(256);
        _ = builder.HasIndex(p => new { p.IsGuest, p.LastActiveAt });
        // Stored as smallint (the enum value): compact, and the numbers are append-only.
        _ = builder.Property(p => p.Language).HasConversion<short>();
        _ = builder.Property(p => p.Role).HasConversion<short>().HasDefaultValue(PlayerRole.Player);
        _ = builder.HasOne(p => p.State).WithOne().HasForeignKey<PlayerState>(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
