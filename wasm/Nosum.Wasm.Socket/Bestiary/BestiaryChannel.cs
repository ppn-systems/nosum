// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Combat;

namespace Nosum.Wasm.Socket.Bestiary;

/// <summary>The player's hunting log as the server last sent it.</summary>
/// <param name="Equipped">The equipped slayer title's species, or <see cref="MonsterKind.None"/>.</param>
/// <param name="Kills">The kill count of each species the player has hunted.</param>
public sealed record BestiaryInfo(MonsterKind Equipped, IReadOnlyDictionary<MonsterKind, int> Kills)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly BestiaryInfo None = new(MonsterKind.None, new Dictionary<MonsterKind, int>());

    /// <summary>Gets how many monsters of a species the player has slain.</summary>
    /// <param name="species">The species.</param>
    /// <returns>The count, zero when none.</returns>
    public int KillsOf(MonsterKind species) => this.Kills.TryGetValue(species, out int kills) ? kills : 0;
}

/// <summary>
/// The hunting log's connection: reads the kill counts and asks the server to equip or take off a slayer title. The server decides every outcome,
/// and kills are never sent by the client (docs/adr/ADR-068).
/// </summary>
public sealed class BestiaryChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public BestiaryChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the log changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to an equip request. May run on any thread.</summary>
    public event Action<BestiaryStatus>? Completed;

    /// <summary>Gets the hunting log as the server last sent it.</summary>
    public BestiaryInfo Current { get; private set; } = BestiaryInfo.None;

    /// <summary>Reads the hunting log.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Equips the slayer title of a species, or takes the title off with <see cref="MonsterKind.None"/>.</summary>
    /// <param name="species">The species.</param>
    public void Equip(MonsterKind species) => _ = this.EquipAsync(species);

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the panel opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetBestiary> request = PacketFactory<GetBestiary>.Acquire();
            BestiarySnapshot snapshot = await _socket.RequestAsync<BestiarySnapshot>(request.Value).ConfigureAwait(false);
            Dictionary<MonsterKind, int> kills = new(snapshot.Rows.Count);
            foreach (BestiaryRow row in snapshot.Rows)
            {
                kills[(MonsterKind)row.Species] = row.Kills;
            }

            this.Current = new BestiaryInfo((MonsterKind)snapshot.EquippedSpecies, kills);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task EquipAsync(MonsterKind species)
    {
        try
        {
            using PacketScope<EquipBestiaryTitle> request = PacketFactory<EquipBestiaryTitle>.Acquire();
            request.Value.Species = (byte)species;
            EquipBestiaryTitleResult reply = await _socket.RequestAsync<EquipBestiaryTitleResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
            await this.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
