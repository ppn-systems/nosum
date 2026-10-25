// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerSessionTokenConfiguration : IEntityTypeConfiguration<PlayerSessionToken>
{
    public void Configure(EntityTypeBuilder<PlayerSessionToken> builder)
    {
        _ = builder.ToTable("player_session_tokens");
        _ = builder.HasKey(t => t.Id);
        _ = builder.Property(t => t.Id).ValueGeneratedNever();
        _ = builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(t => t.TokenHash).IsUnique();
        _ = builder.HasIndex(t => t.PlayerId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(t => t.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
