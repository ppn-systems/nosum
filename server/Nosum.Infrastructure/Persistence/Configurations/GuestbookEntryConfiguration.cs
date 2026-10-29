// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Visits;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class GuestbookEntryConfiguration : IEntityTypeConfiguration<GuestbookEntry>
{
    public void Configure(EntityTypeBuilder<GuestbookEntry> builder)
    {
        _ = builder.ToTable("guestbook_entries");
        _ = builder.HasKey(e => new { e.OwnerId, e.VisitorId, e.DayNumber });
        _ = builder.HasIndex(e => e.VisitorId);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(e => e.VisitorId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_guestbook_entries_not_self", "owner_id <> visitor_id"));
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_guestbook_entries_waters_positive", "waters > 0"));
    }
}
