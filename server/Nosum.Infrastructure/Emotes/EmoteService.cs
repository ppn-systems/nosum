// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Emotes;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.S2C;
using Nosum.Gameplay.Emotes;

namespace Nosum.Infrastructure.Emotes;

/// <summary>
/// <see cref="IEmoteService"/> (docs/adr/ADR-071 D1-D2). The code is checked, the sender pays the cooldown, and the emote goes to the
/// players in the sender's chunk interest set (docs/adr/ADR-022): nearby players only, the same reach as local chat. The sender's own
/// bubble is not pushed back; the client shows it from the <c>EmoteResult</c>.
/// </summary>
public sealed class EmoteService : IEmoteService
{
    private readonly IMapPresence _presence;
    private readonly IChunkPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, EmoteCooldown> _cooldowns = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who is on which map and where.</param>
    /// <param name="publisher">Delivers the emote to the players who can see the sender.</param>
    /// <param name="time">The clock for the cooldown; tests pass a fake one.</param>
    public EmoteService(IMapPresence presence, IChunkPublisher publisher, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _publisher = publisher;
        _time = time;
    }

    /// <inheritdoc/>
    public async ValueTask<EmoteStatus> SendAsync(IConnection connection, byte emote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!EmoteRules.IsValid(emote))
        {
            return EmoteStatus.InvalidEmote;
        }

        if (!_presence.TryGet(connection, out PlayerPresence? me))
        {
            return EmoteStatus.NotOnMap;
        }

        if (!_cooldowns.GetOrAdd(me.AccountId, static _ => new EmoteCooldown()).TryTake(_time.GetUtcNow().ToUnixTimeMilliseconds()))
        {
            return EmoteStatus.Cooldown;
        }

        using PacketScope<EmoteShown> lease = PacketFactory<EmoteShown>.Acquire();
        lease.Value.PlayerId = me.PlayerId;
        lease.Value.Emote = emote;

        if (me.Viewer is { } viewer)
        {
            await _publisher.PublishAsync(me.Instance, viewer.Chunk, lease.Value, connection, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Before the first snapshot tick the player is in no interest set yet: the whole place hears it, as the look does.
            await _publisher.PublishInstanceAsync(me.Instance, lease.Value, connection, cancellationToken).ConfigureAwait(false);
        }

        return EmoteStatus.Ok;
    }
}
