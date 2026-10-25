// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;

namespace Nosum.Wasm.Socket.Blacksmith;

/// <summary>What the blacksmith holds for the player, and how the last request ended.</summary>
/// <param name="Status">The outcome of the last request.</param>
/// <param name="ToolItemId">The upgraded tool being made; zero when the smith holds nothing.</param>
/// <param name="ReadyDay">The game day number the tool can be collected.</param>
/// <param name="Today">The game day number on the server.</param>
public readonly record struct BlacksmithInfo(BlacksmithStatus Status, int ToolItemId, long ReadyDay, long Today);

/// <summary>
/// Talks to the blacksmith (docs/adr/ADR-026 D2): reads the tool the smith holds, hands a tool in and collects it. The prices and the two days are shared rules
/// (<c>BlacksmithRules</c>); the server decides every request. The answer to each arrives as <see cref="Updated"/>.
/// </summary>
public sealed class BlacksmithChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public BlacksmithChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised with the server's answer to a request and the state after it. May run on any thread.</summary>
    public event Action<BlacksmithInfo>? Updated;

    /// <summary>Reads the tool the smith holds for the player.</summary>
    public void Load() => _ = this.SendAsync<GetBlacksmith>(static _ => { });

    /// <summary>Hands a tool in for an upgrade.</summary>
    /// <param name="itemId">The item id of the tool.</param>
    public void Upgrade(int itemId) => _ = this.SendAsync<UpgradeTool>(r => r.ItemId = itemId);

    /// <summary>Collects the upgraded tool.</summary>
    public void Collect() => _ = this.SendAsync<CollectTool>(static _ => { });

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill)
        where TRequest : PacketBase<TRequest>, IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            BlacksmithState reply = await _socket.RequestAsync<BlacksmithState>(request.Value).ConfigureAwait(false);
            this.Updated?.Invoke(new BlacksmithInfo(reply.Status, reply.ToolItemId, reply.ReadyDay, reply.Today));
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
