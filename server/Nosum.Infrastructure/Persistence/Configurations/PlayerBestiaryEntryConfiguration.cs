// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Bestiary;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerBestiaryEntryConfiguration : IEntityTypeConfiguration<PlayerBestiaryEntry>
{
    public void Configure(EntityTypeBuilder<PlayerBestiaryEntry> builder)
    {
        _ = builder.ToTable("player_bestiary");
        _ = builder.HasKey(e => new { e.PlayerId, e.Species });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_player_bestiary_kills_positive", "kills > 0"));
    }
}
