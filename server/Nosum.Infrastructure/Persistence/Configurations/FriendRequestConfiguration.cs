// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        _ = builder.ToTable("friend_requests");
        _ = builder.HasKey(r => new { r.FromId, r.ToId });
        _ = builder.HasIndex(r => r.ToId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.FromId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.ToId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_friend_requests_not_self", "from_id <> to_id"));
    }
}
