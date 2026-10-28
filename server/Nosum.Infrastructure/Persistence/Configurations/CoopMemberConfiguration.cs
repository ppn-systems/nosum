// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopMemberConfiguration : IEntityTypeConfiguration<CoopMember>
{
    public void Configure(EntityTypeBuilder<CoopMember> builder)
    {
        _ = builder.ToTable("coop_members");
        _ = builder.HasKey(m => m.Id);
        _ = builder.Property(m => m.Id).ValueGeneratedNever();
        _ = builder.HasIndex(m => m.PlayerId).IsUnique();
        _ = builder.HasIndex(m => m.CoopId);
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(m => m.CoopId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(m => m.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
