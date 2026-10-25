// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Chat;

namespace Nosum.Abstractions.Chat;

/// <summary>
/// Chat (docs/adr/ADR-029 D1-D3): local, map, co-op and whisper channels. The service cleans and masks the text, applies the rate rule and
/// the block lists, and pushes <c>ChatMessage</c> to the recipients. Chat history is never stored; only a report keeps the last lines
/// the reporter had received.
/// </summary>
public interface IChatService
{
    /// <summary>Sends a message.</summary>
    /// <param name="connection">The sender's connection (which map they are on).</param>
    /// <param name="senderId">The sender's account id.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="target">The nickname for a whisper; ignored for the other channels.</param>
    /// <param name="text">The text as typed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<ChatStatus> SendAsync(IConnection connection, Guid senderId, ChatChannel channel, string? target, string? text, CancellationToken cancellationToken = default);

    /// <summary>Reports a player, storing the last messages the reporter received as context for the admins.</summary>
    /// <param name="reporterId">The reporting player.</param>
    /// <param name="nickname">The nickname of the player reported.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<ChatStatus> ReportAsync(Guid reporterId, string nickname, CancellationToken cancellationToken = default);
}
