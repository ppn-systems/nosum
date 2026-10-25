// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Nodes;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class WorldNodeConfiguration : IEntityTypeConfiguration<WorldNode>
{
    public void Configure(EntityTypeBuilder<WorldNode> builder)
    {
        _ = builder.ToTable("resource_nodes");
        _ = builder.HasKey(n => n.Id);
        _ = builder.Property(n => n.Id).ValueGeneratedOnAdd();
        _ = builder.Property(n => n.MapId).HasMaxLength(64).IsRequired();

        // One node per tile of an instance; the database refuses a double even if two code paths race.
        _ = builder.HasIndex(n => new { n.MapId, n.OwnerId, n.TileX, n.TileY }).IsUnique();
    }
}

internal sealed class NodeInstanceStateConfiguration : IEntityTypeConfiguration<NodeInstanceState>
{
    public void Configure(EntityTypeBuilder<NodeInstanceState> builder)
    {
        _ = builder.ToTable("node_instances");
        _ = builder.HasKey(n => n.Id);
        _ = builder.Property(n => n.Id).ValueGeneratedOnAdd();
        _ = builder.Property(n => n.MapId).HasMaxLength(64).IsRequired();
        _ = builder.HasIndex(n => new { n.MapId, n.OwnerId }).IsUnique();
    }
}
