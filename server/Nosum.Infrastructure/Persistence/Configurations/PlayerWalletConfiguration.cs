// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nosum.Domain.Accounts;
using Nosum.Domain.Economy;

namespace Nosum.Infrastructure.Persistence.Configurations;

internal sealed class PlayerWalletConfiguration : IEntityTypeConfiguration<PlayerWallet>
{
    public void Configure(EntityTypeBuilder<PlayerWallet> builder)
    {
        _ = builder.ToTable("player_wallets");
        _ = builder.HasKey(w => w.PlayerId);
        _ = builder.Property(w => w.PlayerId).ValueGeneratedNever();
        _ = builder.Property(w => w.Coins).IsConcurrencyToken();
        _ = builder.HasOne<Player>().WithMany().HasForeignKey(w => w.PlayerId).OnDelete(DeleteBehavior.Cascade);
        _ = builder.ToTable(t => t.HasCheckConstraint("ck_player_wallets_coins", "coins >= 0"));
    }
}
