// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class BlockedPlayerConfiguration : IEntityTypeConfiguration<BlockedPlayer>
{
    public void Configure(EntityTypeBuilder<BlockedPlayer> builder)
    {
        _ = builder.ToTable("blocked_players");
        _ = builder.HasKey(b => new { b.OwnerId, b.BlockedId });
        _ = builder.HasIndex(b => b.BlockedId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_blocked_players_not_self", "owner_id <> blocked_id"));
    }
}
