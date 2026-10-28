// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        _ = builder.ToTable("friendships");
        _ = builder.HasKey(f => new { f.PlayerId, f.FriendId });
        _ = builder.HasIndex(f => f.FriendId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(f => f.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(f => f.FriendId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_friendships_not_self", "player_id <> friend_id"));
    }
}
