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
        _ = builder.Property(p => p.Username).HasMaxLength(AccountLimits.UsernameMaxLength).IsRequired();
        _ = builder.Property(p => p.NormalizedUsername).HasMaxLength(AccountLimits.UsernameMaxLength).IsRequired();
        _ = builder.HasIndex(p => p.NormalizedUsername).IsUnique();
        _ = builder.Property(p => p.PasswordHash).HasMaxLength(256).IsRequired();
        _ = builder.Property(p => p.PreferredLanguage).HasMaxLength(AccountLimits.LanguageMaxLength).IsRequired();
        _ = builder.HasOne(p => p.State).WithOne().HasForeignKey<PlayerState>(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
