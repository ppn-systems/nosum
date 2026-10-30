// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Pets;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerPetConfiguration : IEntityTypeConfiguration<PlayerPet>
{
    public void Configure(EntityTypeBuilder<PlayerPet> builder)
    {
        _ = builder.ToTable("player_pets");
        _ = builder.HasKey(p => p.PlayerId);
        _ = builder.Property(p => p.PlayerId).ValueGeneratedNever();
        _ = builder.Property(p => p.Kind).IsRequired();
        _ = builder.Property(p => p.Name).HasMaxLength(64).IsRequired();
        _ = builder.Property(p => p.Mood).IsRequired();
        _ = builder.Property(p => p.LastPettedDay).IsRequired();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
