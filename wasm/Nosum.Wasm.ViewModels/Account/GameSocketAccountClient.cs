// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Wasm.Socket;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// <see cref="IAccountClient"/> over the game's single socket. Waits for the connection to be ready
/// (it may still be connecting when the player submits) and turns transport failures into
/// <see cref="AccountConnectionException"/>.
/// </summary>
public sealed class GameSocketAccountClient : IAccountClient
{
    private static readonly TimeSpan s_connectWait = TimeSpan.FromSeconds(8);

    private readonly IGameSocket _socket;

    /// <summary>Initializes the client.</summary>
    /// <param name="socket">The game connection.</param>
    public GameSocketAccountClient(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <inheritdoc/>
    public async Task<AccountReply> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, CancellationToken cancellationToken)
    {
        using PacketScope<ExternalSignIn> lease = PacketFactory<ExternalSignIn>.Acquire();
        lease.Value.Provider = provider;
        lease.Value.IdToken = idToken;
        lease.Value.PreferredLanguage = language;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
    {
        using PacketScope<StartGuest> lease = PacketFactory<StartGuest>.Acquire();
        lease.Value.PreferredLanguage = language;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountReply> LinkExternalIdentityAsync(ExternalProvider provider, string idToken, string nickname, CancellationToken cancellationToken)
    {
        using PacketScope<LinkExternalIdentity> lease = PacketFactory<LinkExternalIdentity>.Acquire();
        lease.Value.Provider = provider;
        lease.Value.IdToken = idToken;
        lease.Value.Nickname = nickname;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken)
    {
        using PacketScope<SetNickname> lease = PacketFactory<SetNickname>.Acquire();
        lease.Value.Nickname = nickname;
        try
        {
            await _socket.WaitReadyAsync(cancellationToken).WaitAsync(s_connectWait, cancellationToken).ConfigureAwait(false);
            NicknameResult result = await _socket.RequestAsync<NicknameResult>(lease.Value, cancellationToken).ConfigureAwait(false);
            return new NicknameReply(result.Status, result.Nickname, result.NextChangeAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(result.NextChangeAtMs) : null);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            throw new AccountConnectionException("The nickname request could not be completed.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountStatus> SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken)
    {
        using PacketScope<SetLanguage> lease = PacketFactory<SetLanguage>.Acquire();
        lease.Value.Language = language;
        try
        {
            await _socket.WaitReadyAsync(cancellationToken).WaitAsync(s_connectWait, cancellationToken).ConfigureAwait(false);
            LanguageResult result = await _socket.RequestAsync<LanguageResult>(lease.Value, cancellationToken).ConfigureAwait(false);
            return result.Status;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            throw new AccountConnectionException("The language request could not be completed.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        using PacketScope<Logout> lease = PacketFactory<Logout>.Acquire();
        _ = await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
    {
        using PacketScope<ResumeSession> lease = PacketFactory<ResumeSession>.Acquire();
        lease.Value.SessionToken = sessionToken;
        return await this.SendAsync(lease.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountReply> SendAsync(Nalix.Abstractions.Networking.Packets.IPacket request, CancellationToken cancellationToken)
    {
        try
        {
            await _socket.WaitReadyAsync(cancellationToken).WaitAsync(s_connectWait, cancellationToken).ConfigureAwait(false);
            AccountResult result = await _socket.RequestAsync<AccountResult>(request, cancellationToken).ConfigureAwait(false);

            AccountSession? session = result.Status == AccountStatus.Ok
                ? new AccountSession(result.PlayerId, result.Email, result.SessionToken, DateTimeOffset.FromUnixTimeSeconds(result.ExpiresAtUnixSeconds), result.PreferredLanguage, result.IsGuest, result.RegistrationPromptUnlocked, result.Nickname)
                : null;
            return new AccountReply(result.Status, session);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            throw new AccountConnectionException("The account request could not be completed.", ex);
        }
    }
}
