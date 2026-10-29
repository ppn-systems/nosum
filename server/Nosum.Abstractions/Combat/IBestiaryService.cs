// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Combat;

namespace Nosum.Abstractions.Combat;

/// <summary>One species and the kills of it, as the hunting log reads it.</summary>
/// <param name="Species">The monster species.</param>
/// <param name="Kills">How many monsters of the species were slain.</param>
public sealed record BestiaryLine(MonsterKind Species, int Kills);

/// <summary>What one read of the hunting log returns.</summary>
/// <param name="Equipped">The equipped title's species, or <see cref="MonsterKind.None"/>.</param>
/// <param name="Lines">The species the player has hunted, lowest species first.</param>
public sealed record BestiaryView(MonsterKind Equipped, IReadOnlyList<BestiaryLine> Lines)
{
    /// <summary>Copies the view into a snapshot packet (the packet's list is cleared first).</summary>
    /// <param name="packet">The packet to fill.</param>
    public void FillSnapshot(BestiarySnapshot packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        packet.EquippedSpecies = (byte)this.Equipped;
        packet.Rows.Clear();
        foreach (BestiaryLine line in this.Lines)
        {
            packet.Rows.Add(new BestiaryRow { Species = (byte)line.Species, Kills = line.Kills });
        }
    }
}

/// <summary>
/// The hunting log (docs/adr/ADR-068): counts the monsters a player slays, and lets the player equip a slayer title once a species' last milestone
/// is reached. Only the monster defeat path calls <see cref="RecordKillAsync"/>.
/// </summary>
public interface IBestiaryService
{
    /// <summary>Counts one more monster of a species the player slew.</summary>
    /// <param name="playerId">The player who slew it.</param>
    /// <param name="species">The species.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the count is saved.</returns>
    Task RecordKillAsync(Guid playerId, MonsterKind species, CancellationToken cancellationToken = default);

    /// <summary>Reads the player's hunting log.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The kill counts and the equipped title.</returns>
    Task<BestiaryView> ReadAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Equips a slayer title the player has earned, or takes the title off with <see cref="MonsterKind.None"/>.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="species">The species of the title.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The outcome.</returns>
    Task<BestiaryStatus> EquipTitleAsync(Guid playerId, MonsterKind species, CancellationToken cancellationToken = default);
}
