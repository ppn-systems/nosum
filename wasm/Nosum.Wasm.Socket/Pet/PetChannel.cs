// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Gameplay.Pets;

namespace Nosum.Wasm.Socket.Pet;

/// <summary>The owner's pet as the server last answered: kind, name, mood and whether it was petted today. Kind None means no pet.</summary>
/// <param name="Status">The outcome of the request that produced it.</param>
/// <param name="Kind">The pet's kind; <see cref="PetKind.None"/> without a pet.</param>
/// <param name="Name">The cleaned name.</param>
/// <param name="Mood">The mood, 0 to <see cref="PetRules.MaxMood"/>.</param>
/// <param name="PettedToday">Whether the pet was petted on the current game day.</param>
public readonly record struct PetInfo(PetStatus Status, PetKind Kind, string Name, int Mood, bool PettedToday)
{
    /// <summary>Gets the state of a player without a pet.</summary>
    public static PetInfo None => new(PetStatus.Ok, PetKind.None, string.Empty, 0, false);

    /// <summary>Gets whether the player has a pet.</summary>
    public bool HasPet => this.Kind != PetKind.None;
}

/// <summary>
/// The pet side of the connection (docs/adr/ADR-075 D3-D5): reads, adopts and pets the owner's companion. The server owns the state (kind, name,
/// mood, petting); every reply carries the pet as it now stands, and this channel keeps the last one for the panel.
/// </summary>
public sealed class PetChannel
{
    private readonly IGameSocket _socket;
    private readonly Lock _gate = new();
    private PetInfo _current = PetInfo.None;
    private bool _loaded;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public PetChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the pet state changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to an adoption or a petting. May run on any thread.</summary>
    public event Action<PetStatus>? Completed;

    /// <summary>Gets the owner's pet as the server last answered.</summary>
    public PetInfo Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Gets whether the server has answered a pet request yet.</summary>
    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    /// <summary>Reads the owner's pet. Call whenever the connection is signed in.</summary>
    public void Load() => _ = this.RequestAsync<GetPet>(static _ => { }, announce: false);

    /// <summary>Adopts a cat or a dog and names it. The server refuses a second pet and an invalid kind or name.</summary>
    /// <param name="kind">The kind: 1 cat, 2 dog.</param>
    /// <param name="name">The name as typed; the server cleans it.</param>
    public void Adopt(byte kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _ = this.RequestAsync<AdoptPet>(r =>
        {
            r.Kind = kind;
            r.Name = name;
        }, announce: true);
    }

    /// <summary>Pets the owner's pet, once a game day.</summary>
    public void Pet() => _ = this.RequestAsync<PetPet>(static _ => { }, announce: true);

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task RequestAsync<TRequest>(Action<TRequest> fill, bool announce)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            PetState reply = await _socket.RequestAsync<PetState>(request.Value).ConfigureAwait(false);
            lock (_gate)
            {
                _current = new PetInfo(reply.Status, (PetKind)reply.Kind, reply.Name, reply.Mood, reply.PettedToday);
                _loaded = true;
            }

            this.Changed?.Invoke();
            if (announce)
            {
                this.Completed?.Invoke(reply.Status);
            }
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
