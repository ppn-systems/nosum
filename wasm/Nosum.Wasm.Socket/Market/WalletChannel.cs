// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;

namespace Nosum.Wasm.Socket.Market;

/// <summary>The client's mirror of the player's coin balance: read at sign-in, then replaced by every server push.</summary>
public sealed class WalletChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _updates;
    private long _coins;

    /// <summary>Initializes the channel and starts listening for balance pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public WalletChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _updates = socket.Subscribe<WalletUpdate>(p => this.Set(p.Coins));
    }

    /// <summary>Raised when the balance changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the balance; zero until the first read.</summary>
    public long Coins => Interlocked.Read(ref _coins);

    /// <summary>Reads the balance. Call whenever the connection is signed in.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <inheritdoc/>
    public void Dispose() => _updates.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A failed read is retried at the next sign-in; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetWallet> request = PacketFactory<GetWallet>.Acquire();
            WalletSnapshot snapshot = await _socket.RequestAsync<WalletSnapshot>(request.Value).ConfigureAwait(false);
            this.Set(snapshot.Coins);
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    private void Set(long coins)
    {
        Interlocked.Exchange(ref _coins, coins);
        this.Changed?.Invoke();
    }
}
