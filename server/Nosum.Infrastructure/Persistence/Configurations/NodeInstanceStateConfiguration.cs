// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Nodes;

namespace Nosum.Infrastructure.Persistence.Configurations;

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
