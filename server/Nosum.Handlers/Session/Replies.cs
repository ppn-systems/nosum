// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;

namespace Nosum.Handlers.Session;

/// <summary>The pooled-reply pattern every handler repeats: rent, fill, reply with the request's sequence id, return to the pool.</summary>
internal static class Replies
{
    /// <summary>Replies to a request with a pooled packet.</summary>
    /// <typeparam name="TRequest">The request packet type.</typeparam>
    /// <typeparam name="TReply">The reply packet type.</typeparam>
    /// <param name="context">The request context.</param>
    /// <param name="sequence">The request's sequence id (copied before any await).</param>
    /// <param name="fill">Fills the reply.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    public static async ValueTask ReplyAsync<TRequest, TReply>(IPacketContext<TRequest> context, ushort sequence, Action<TReply> fill)
        where TRequest : class, IPacket
        where TReply : PacketBase<TReply>, IPacketStaticOpcode, new()
    {
        PacketScope<TReply> lease = PacketFactory<TReply>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            fill(lease.Value);
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
