// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class ChatReportConfiguration : IEntityTypeConfiguration<ChatReport>
{
    public void Configure(EntityTypeBuilder<ChatReport> builder)
    {
        _ = builder.ToTable("chat_reports");
        _ = builder.HasKey(r => r.Id);
        _ = builder.Property(r => r.Id).ValueGeneratedNever();
        _ = builder.HasIndex(r => new { r.ReportedId, r.CreatedAt });
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.ReportedId).OnDelete(DeleteBehavior.Cascade);
    }
}
