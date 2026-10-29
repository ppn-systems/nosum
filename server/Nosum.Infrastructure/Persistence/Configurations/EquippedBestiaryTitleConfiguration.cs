// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Bestiary;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class EquippedBestiaryTitleConfiguration : IEntityTypeConfiguration<EquippedBestiaryTitle>
{
    public void Configure(EntityTypeBuilder<EquippedBestiaryTitle> builder)
    {
        _ = builder.ToTable("equipped_bestiary_titles");
        _ = builder.HasKey(t => t.PlayerId);
        _ = builder.Property(t => t.PlayerId).ValueGeneratedNever();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(t => t.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_equipped_bestiary_titles_species", "species > 0"));
    }
}
