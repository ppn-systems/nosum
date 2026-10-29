// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Community;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CommunityUnlockRecordConfiguration : IEntityTypeConfiguration<CommunityUnlockRecord>
{
    public void Configure(EntityTypeBuilder<CommunityUnlockRecord> builder)
    {
        _ = builder.ToTable("community_unlocks");
        _ = builder.HasKey(u => u.Key);
        _ = builder.Property(u => u.Key).HasMaxLength(64).ValueGeneratedNever();
    }
}
