// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerStateConfiguration : IEntityTypeConfiguration<PlayerState>
{
    public void Configure(EntityTypeBuilder<PlayerState> builder)
    {
        _ = builder.ToTable("player_states");
        _ = builder.HasKey(s => s.PlayerId);
        _ = builder.Property(s => s.PlayerId).ValueGeneratedNever();
        _ = builder.Property(s => s.MapName).HasMaxLength(64).IsRequired();
    }
}
