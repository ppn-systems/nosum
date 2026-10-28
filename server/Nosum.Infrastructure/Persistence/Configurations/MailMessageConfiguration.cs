// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Mail;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class MailMessageConfiguration : IEntityTypeConfiguration<MailMessage>
{
    public void Configure(EntityTypeBuilder<MailMessage> builder)
    {
        _ = builder.ToTable("mail_messages");
        _ = builder.HasKey(m => m.Id);
        _ = builder.Property(m => m.Id).ValueGeneratedNever();
        _ = builder.Property(m => m.Code).HasMaxLength(64).IsRequired();
        _ = builder.Property(m => m.Args).IsRequired();
        _ = builder.Property(m => m.CollectedAt).IsConcurrencyToken();
        _ = builder.HasIndex(m => new { m.RecipientId, m.SentAt });
        _ = builder.HasIndex(m => m.ExpiresAt);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(m => m.RecipientId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_mail_messages_coins", "coins >= 0"));
    }
}
