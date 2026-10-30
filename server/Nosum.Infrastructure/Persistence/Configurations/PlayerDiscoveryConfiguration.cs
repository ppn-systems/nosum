// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Journal;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerDiscoveryConfiguration : IEntityTypeConfiguration<PlayerDiscovery>
{
    public void Configure(EntityTypeBuilder<PlayerDiscovery> builder)
    {
        _ = builder.ToTable("player_discoveries");
        _ = builder.HasKey(d => new { d.PlayerId, d.ItemId });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(d => d.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_player_discoveries_count_positive", "count > 0"));
    }
}
