// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Requests;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class DailyRequestConfiguration : IEntityTypeConfiguration<DailyRequest>
{
    public void Configure(EntityTypeBuilder<DailyRequest> builder)
    {
        _ = builder.ToTable("daily_requests");
        _ = builder.HasKey(r => new { r.Day, r.Slot });
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_daily_requests_amounts", "quantity > 0 AND reward_coins >= 0"));
    }
}
