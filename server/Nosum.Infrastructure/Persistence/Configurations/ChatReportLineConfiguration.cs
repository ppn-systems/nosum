// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class ChatReportLineConfiguration : IEntityTypeConfiguration<ChatReportLine>
{
    public void Configure(EntityTypeBuilder<ChatReportLine> builder)
    {
        _ = builder.ToTable("chat_report_lines");
        _ = builder.HasKey(l => new { l.ReportId, l.Sequence });
        _ = builder.Property(l => l.SenderNickname).HasMaxLength(32).IsRequired();
        _ = builder.Property(l => l.Text).HasMaxLength(640).IsRequired();
        _ = builder.HasOne<ChatReport>().WithMany().HasForeignKey(l => l.ReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
