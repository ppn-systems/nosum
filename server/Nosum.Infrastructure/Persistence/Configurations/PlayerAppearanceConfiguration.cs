// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Looks;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerAppearanceConfiguration : IEntityTypeConfiguration<PlayerAppearance>
{
    public void Configure(EntityTypeBuilder<PlayerAppearance> builder)
    {
        _ = builder.ToTable("player_appearances");
        _ = builder.HasKey(a => a.PlayerId);
        _ = builder.Property(a => a.PlayerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
