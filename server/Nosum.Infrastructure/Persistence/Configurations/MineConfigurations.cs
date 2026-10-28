// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Mines;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerMineConfiguration : IEntityTypeConfiguration<PlayerMine>
{
    public void Configure(EntityTypeBuilder<PlayerMine> builder)
    {
        _ = builder.ToTable("player_mines");
        _ = builder.HasKey(m => m.PlayerId);
        _ = builder.Property(m => m.PlayerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(m => m.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_player_mines_deepest_floor", "deepest_floor >= 0"));
    }
}
