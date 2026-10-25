// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Quests;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerQuestConfiguration : IEntityTypeConfiguration<PlayerQuest>
{
    public void Configure(EntityTypeBuilder<PlayerQuest> builder)
    {
        _ = builder.ToTable("player_quests");
        _ = builder.HasKey(q => new { q.PlayerId, q.QuestId });
        // Stored as smallint (the enum value): compact, and the numbers are append-only.
        _ = builder.Property(q => q.State).HasConversion<short>();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(q => q.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
