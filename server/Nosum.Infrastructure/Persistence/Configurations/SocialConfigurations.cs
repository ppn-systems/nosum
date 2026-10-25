// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Mail;
using Nosum.Domain.Social;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class BlockedPlayerConfiguration : IEntityTypeConfiguration<BlockedPlayer>
{
    public void Configure(EntityTypeBuilder<BlockedPlayer> builder)
    {
        _ = builder.ToTable("blocked_players");
        _ = builder.HasKey(b => new { b.OwnerId, b.BlockedId });
        _ = builder.HasIndex(b => b.BlockedId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_blocked_players_not_self", "owner_id <> blocked_id"));
    }
}

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

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        _ = builder.ToTable("friendships");
        _ = builder.HasKey(f => new { f.PlayerId, f.FriendId });
        _ = builder.HasIndex(f => f.FriendId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(f => f.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(f => f.FriendId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_friendships_not_self", "player_id <> friend_id"));
    }
}

internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        _ = builder.ToTable("friend_requests");
        _ = builder.HasKey(r => new { r.FromId, r.ToId });
        _ = builder.HasIndex(r => r.ToId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.FromId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(r => r.ToId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_friend_requests_not_self", "from_id <> to_id"));
    }
}

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
