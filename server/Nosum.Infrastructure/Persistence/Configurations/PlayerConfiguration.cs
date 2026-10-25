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
        _ = builder.Property(p => p.PasswordHash).HasMaxLength(256);
        _ = builder.HasIndex(p => new { p.IsGuest, p.LastActiveAt });
        _ = builder.Property(p => p.PreferredLanguage).HasMaxLength(AccountLimits.LanguageMaxLength).IsRequired();
        _ = builder.HasOne(p => p.State).WithOne().HasForeignKey<PlayerState>(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
