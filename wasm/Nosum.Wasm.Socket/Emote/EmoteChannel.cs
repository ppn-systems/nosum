// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;

namespace Nosum.Wasm.Socket.Emote;

/// <summary>
/// The emote side of the connection (docs/adr/ADR-071 D1-D2): sends the emote the player chose and receives the emotes other farmers show near
/// them. The server checks the code and the cooldown and decides every outcome; a refused emote changes nothing.
/// </summary>
public sealed class EmoteChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _shown;

    /// <summary>Initializes the channel and starts listening for emotes shown by other farmers.</summary>
    /// <param name="socket">The game connection.</param>
    public EmoteChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _shown = socket.Subscribe<EmoteShown>(p => this.RemoteShown?.Invoke(p.PlayerId, p.Emote));
    }

    /// <summary>Raised when another farmer near the player shows an emote: their player id and the emote code. May run on any thread.</summary>
    public event Action<uint, byte>? RemoteShown;

    /// <summary>Raised with the server's answer to the player's own emote. May run on any thread.</summary>
    public event Action<EmoteStatus>? Completed;

    /// <summary>Sends an emote; the answer comes through <see cref="Completed"/>.</summary>
    /// <param name="emote">The emote code, 0 to <c>EmoteRules.Count</c> minus one.</param>
    public void Send(byte emote) => _ = this.SendAsync(emote);

    /// <inheritdoc/>
    public void Dispose() => _shown.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player chooses again.")]
    private async Task SendAsync(byte emote)
    {
        try
        {
            using PacketScope<SendEmote> request = PacketFactory<SendEmote>.Acquire();
            request.Value.Emote = emote;
            EmoteResult reply = await _socket.RequestAsync<EmoteResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
