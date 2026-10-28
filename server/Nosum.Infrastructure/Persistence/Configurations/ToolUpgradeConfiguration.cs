// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Blacksmith;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class ToolUpgradeConfiguration : IEntityTypeConfiguration<ToolUpgrade>
{
    public void Configure(EntityTypeBuilder<ToolUpgrade> builder)
    {
        _ = builder.ToTable("tool_upgrades");
        _ = builder.HasKey(u => u.PlayerId);
        _ = builder.Property(u => u.PlayerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(u => u.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
