// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Mail;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class MailAttachmentItemConfiguration : IEntityTypeConfiguration<MailAttachmentItem>
{
    public void Configure(EntityTypeBuilder<MailAttachmentItem> builder)
    {
        _ = builder.ToTable("mail_attachment_items");
        _ = builder.HasKey(i => new { i.MailId, i.ItemId });
        _ = builder.HasOne<MailMessage>().WithMany().HasForeignKey(i => i.MailId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_mail_attachment_items_quantity", "quantity > 0"));
    }
}
