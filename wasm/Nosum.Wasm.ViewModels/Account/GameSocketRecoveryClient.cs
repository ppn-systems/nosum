// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Wasm.Socket;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary><see cref="IRecoveryClient"/> over the game socket.</summary>
public sealed class GameSocketRecoveryClient : IRecoveryClient
{
    private static readonly TimeSpan s_connectWait = TimeSpan.FromSeconds(8);

    private readonly IGameSocket _socket;

    /// <summary>Initializes the client.</summary>
    /// <param name="socket">The game connection.</param>
    public GameSocketRecoveryClient(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        using PacketScope<RequestPasswordReset> lease = PacketFactory<RequestPasswordReset>.Acquire();
        lease.Value.Email = email;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> ConfirmResetAsync(string email, string code, string newPassword, CancellationToken cancellationToken)
    {
        using PacketScope<ConfirmPasswordReset> lease = PacketFactory<ConfirmPasswordReset>.Acquire();
        lease.Value.Email = email;
        lease.Value.Code = code;
        lease.Value.NewPassword = newPassword;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        using PacketScope<ChangePassword> lease = PacketFactory<ChangePassword>.Acquire();
        lease.Value.CurrentPassword = currentPassword;
        lease.Value.NewPassword = newPassword;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountStatus> SendAsync(IPacket request, CancellationToken cancellationToken)
    {
        try
        {
            await _socket.WaitReadyAsync(cancellationToken).WaitAsync(s_connectWait, cancellationToken).ConfigureAwait(false);
            PasswordResult result = await _socket.RequestAsync<PasswordResult>(request, cancellationToken).ConfigureAwait(false);
            return result.Status;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            throw new AccountConnectionException("The recovery request could not be completed.", ex);
        }
    }
}
