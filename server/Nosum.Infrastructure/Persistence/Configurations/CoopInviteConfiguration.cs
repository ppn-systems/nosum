// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopInviteConfiguration : IEntityTypeConfiguration<CoopInvite>
{
    public void Configure(EntityTypeBuilder<CoopInvite> builder)
    {
        _ = builder.ToTable("coop_invites");
        _ = builder.HasKey(i => i.Id);
        _ = builder.Property(i => i.Id).ValueGeneratedNever();
        _ = builder.HasIndex(i => new { i.CoopId, i.PlayerId }).IsUnique();
        _ = builder.HasIndex(i => i.PlayerId);
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(i => i.CoopId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(i => i.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
