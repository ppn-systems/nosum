// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class CoopConfiguration : IEntityTypeConfiguration<Coop>
{
    public void Configure(EntityTypeBuilder<Coop> builder)
    {
        _ = builder.ToTable("coops");
        _ = builder.HasKey(c => c.Id);
        _ = builder.Property(c => c.Id).ValueGeneratedNever();
        _ = builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
    }
}

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

internal sealed class CoopStockItemConfiguration : IEntityTypeConfiguration<CoopStockItem>
{
    public void Configure(EntityTypeBuilder<CoopStockItem> builder)
    {
        _ = builder.ToTable("coop_stock");
        _ = builder.HasKey(s => new { s.CoopId, s.ItemId });
        _ = builder.Property(s => s.Quantity).IsConcurrencyToken();
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(s => s.CoopId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_coop_stock_quantity", "quantity > 0"));
    }
}

internal sealed class CoopLogEntryConfiguration : IEntityTypeConfiguration<CoopLogEntry>
{
    public void Configure(EntityTypeBuilder<CoopLogEntry> builder)
    {
        _ = builder.ToTable("coop_log");
        _ = builder.HasKey(e => e.Id);
        _ = builder.Property(e => e.Id).ValueGeneratedNever();
        _ = builder.Property(e => e.Reason).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(e => new { e.CoopId, e.OccurredAt });
        _ = builder.HasOne<Coop>().WithMany().HasForeignKey(e => e.CoopId).OnDelete(DeleteBehavior.Cascade);
    }
}
