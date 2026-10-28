// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Requests;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class RequestFillConfiguration : IEntityTypeConfiguration<RequestFill>
{
    public void Configure(EntityTypeBuilder<RequestFill> builder)
    {
        _ = builder.ToTable("request_fills");
        _ = builder.HasKey(f => new { f.Day, f.Slot, f.PlayerId });
        _ = builder.HasIndex(f => f.PlayerId);
        _ = builder.HasOne<DailyRequest>().WithMany().HasForeignKey(f => new { f.Day, f.Slot }).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(f => f.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
