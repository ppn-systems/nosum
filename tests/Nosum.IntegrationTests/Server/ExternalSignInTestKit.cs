// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Identity;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Stands in for the Google and Microsoft verifiers (docs/adr/ADR-069): a token <c>fake:&lt;Provider&gt;:&lt;subject&gt;</c> is accepted as that
/// identity, and nothing else is. Signature checking is covered by the verifier unit tests, which need no network.
/// </summary>
internal sealed class FakeExternalTokenVerifier(ExternalProvider provider) : IExternalTokenVerifier
{
    /// <summary>Verifiers for both providers, as the host registers them.</summary>
    /// <returns>One fake verifier per provider.</returns>
    public static IExternalTokenVerifier[] All() => [new FakeExternalTokenVerifier(ExternalProvider.Google), new FakeExternalTokenVerifier(ExternalProvider.Microsoft)];

    /// <summary>Builds the token a fake verifier accepts for one identity.</summary>
    /// <param name="provider">The provider that "issued" it.</param>
    /// <param name="subject">The provider's subject.</param>
    /// <returns>The fake token.</returns>
    public static string Token(ExternalProvider provider, string subject) => "fake:" + provider + ":" + subject;

    /// <inheritdoc/>
    public ExternalProvider Provider { get; } = provider;

    /// <inheritdoc/>
    public Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        string prefix = "fake:" + Provider + ":";
        if (!idToken.StartsWith(prefix, System.StringComparison.Ordinal) || idToken.Length == prefix.Length)
        {
            return Task.FromResult<ExternalIdentity?>(null);
        }

        return Task.FromResult<ExternalIdentity?>(new ExternalIdentity(Provider, idToken[prefix.Length..], null));
    }
}

/// <summary>Account helpers for integration tests, on top of <see cref="FakeExternalTokenVerifier"/>.</summary>
internal static class TestAccounts
{
    /// <summary>
    /// Replaces the host's account service with one that accepts the fake tokens. The host registers the real verifiers, which need the
    /// provider keys over the network; a test that signs in or links over the wire, or through the instance, calls this after <c>HostServices.Register</c>.
    /// </summary>
    /// <param name="options">The database the host uses.</param>
    public static void UseFakeVerifiers(DbContextOptions<NosumDbContext> options)
        => InstanceManager.Instance.Register<IAccountService>(new AccountService(
            new EfUnitOfWorkFactory(options),
            FakeExternalTokenVerifier.All(),
            new SessionTokenService(),
            TimeProvider.System,
            nicknames: InstanceManager.Instance.GetExistingInstance<INicknameDirectory>()));


    /// <summary>Signs in (creating on first use) the account of a Google subject, as the start screen does.</summary>
    /// <param name="accounts">The service under test.</param>
    /// <param name="subject">The Google subject; the account's identity.</param>
    /// <param name="language">The language for a new account.</param>
    /// <returns>The sign-in outcome.</returns>
    public static Task<AccountOutcome> SignInAsync(IAccountService accounts, string subject, GameLanguage language = GameLanguage.English)
        => accounts.SignInWithExternalAsync(ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject), language, CancellationToken.None);

    /// <summary>
    /// A registered account with a chosen nickname: a guest that links a Google identity with that nickname. This replaces the old password registration
    /// in tests that need a known nickname.
    /// </summary>
    /// <param name="accounts">The service under test.</param>
    /// <param name="subject">The Google subject to link.</param>
    /// <param name="nickname">The nickname to choose.</param>
    /// <param name="language">The language of the guest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The link outcome: Ok with the registered session, or the refusal.</returns>
    public static async Task<AccountOutcome> RegisterAsync(IAccountService accounts, string subject, string nickname, GameLanguage language = GameLanguage.English, CancellationToken cancellationToken = default)
    {
        AccountOutcome guest = await accounts.StartGuestAsync(language, cancellationToken).ConfigureAwait(false);
        return await accounts.LinkExternalIdentityAsync(guest.PlayerId, guest.SessionId, ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject), nickname, cancellationToken).ConfigureAwait(false);
    }
}
