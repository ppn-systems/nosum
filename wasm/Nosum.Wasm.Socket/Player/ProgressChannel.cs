// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Wasm.Socket.Player;

/// <summary>
/// The client's mirror of the player's energy and skill experience (docs/adr/ADR-023). The server owns both: it pushes <see cref="EnergyChanged"/> and
/// <see cref="SkillXpChanged"/> after joining and after every change, and <see cref="LevelUp"/> when a skill reaches a new level. Eating is an intent;
/// the answer arrives as <see cref="EatCompleted"/> and the new energy as a push.
/// </summary>
public sealed class ProgressChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private int _energy;
    private int _maxEnergy;
    private int _farmingXp;
    private int _foragingXp;
    private int _miningXp;
    private int _fishingXp;

    /// <summary>Initializes the channel and starts listening for pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public ProgressChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _subscriptions =
        [
            socket.Subscribe<EnergyChanged>(this.OnEnergy),
            socket.Subscribe<SkillXpChanged>(this.OnXp),
            socket.Subscribe<LevelUp>(this.OnLevelUp),
        ];
    }

    /// <summary>Raised when the energy or the experience changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a skill reached a new level: the skill (1 farming, 2 foraging, 3 mining) and the level. May run on any thread.</summary>
    public event Action<byte, int>? LevelledUp;

    /// <summary>Raised when the server answered an eat request. May run on any thread.</summary>
    public event Action<EatStatus>? EatCompleted;

    /// <summary>Gets the energy now; zero until the server has said.</summary>
    public int Energy => Volatile.Read(ref _energy);

    /// <summary>Gets the largest energy; zero until the server has said, which hides the bar.</summary>
    public int MaxEnergy => Volatile.Read(ref _maxEnergy);

    /// <summary>Gets the cumulative farming experience.</summary>
    public int FarmingXp => Volatile.Read(ref _farmingXp);

    /// <summary>Gets the cumulative foraging experience.</summary>
    public int ForagingXp => Volatile.Read(ref _foragingXp);

    /// <summary>Gets the cumulative mining experience.</summary>
    public int MiningXp => Volatile.Read(ref _miningXp);

    /// <summary>Gets the cumulative fishing experience (docs/adr/ADR-078 amendment).</summary>
    public int FishingXp => Volatile.Read(ref _fishingXp);

    /// <summary>Asks the server to eat the food in a hotbar slot; the answer arrives as <see cref="EatCompleted"/>.</summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    public void Eat(int slot) => _ = this.EatAsync(slot);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task EatAsync(int slot)
    {
        try
        {
            using PacketScope<Eat> request = PacketFactory<Eat>.Acquire();
            request.Value.Slot = (byte)slot;
            EatResult result = await _socket.RequestAsync<EatResult>(request.Value).ConfigureAwait(false);
            this.EatCompleted?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was eaten.
        }
    }

    private void OnEnergy(EnergyChanged packet)
    {
        Volatile.Write(ref _energy, packet.Energy);
        Volatile.Write(ref _maxEnergy, packet.MaxEnergy);
        this.Changed?.Invoke();
    }

    private void OnXp(SkillXpChanged packet)
    {
        Volatile.Write(ref _farmingXp, packet.FarmingXp);
        Volatile.Write(ref _foragingXp, packet.ForagingXp);
        Volatile.Write(ref _miningXp, packet.MiningXp);
        Volatile.Write(ref _fishingXp, packet.FishingXp);
        this.Changed?.Invoke();
    }

    private void OnLevelUp(LevelUp packet) => this.LevelledUp?.Invoke(packet.Skill, packet.Level);
}
