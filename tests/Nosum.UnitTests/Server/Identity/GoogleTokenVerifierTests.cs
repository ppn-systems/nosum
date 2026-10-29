// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Identity;
using Nosum.Contracts.Account;
using Nosum.Infrastructure.Identity;
using Xunit;

namespace Nosum.UnitTests.Server.Identity;

/// <summary>Google ID tokens are accepted only when signed by Google's keys, for this client id, from Google's issuer, and in time (docs/adr/ADR-069 D3).</summary>
public sealed class GoogleTokenVerifierTests : IDisposable
{
    private const string ClientId = "1234-test.apps.googleusercontent.com";
    private const string Issuer = "https://accounts.google.com";

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly TokenFactory _keys = new();
    private readonly StubKeySetHandler _keySet;
    private readonly HttpClient _http;
    private readonly MutableClock _clock = new(Now);

    public GoogleTokenVerifierTests()
    {
        _keySet = new StubKeySetHandler(_keys.Jwks());
        _http = new HttpClient(_keySet);
    }

    public void Dispose()
    {
        _http.Dispose();
        _keySet.Dispose();
        _keys.Dispose();
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsSubjectAndEmail()
    {
        string token = _keys.Sign(Claims(subject: "109876543210", email: "player@example.test"));

        ExternalIdentity? identity = await Verifier().VerifyAsync(token, CancellationToken.None);

        identity.Should().Be(new ExternalIdentity(ExternalProvider.Google, "109876543210", "player@example.test"));
    }

    [Fact]
    public async Task VerifyAsync_AudienceListNamingTheClient_ReturnsIdentity()
    {
        JsonArray audiences = new(JsonValue.Create("other-app"), JsonValue.Create(ClientId));
        string token = _keys.Sign(Claims(audience: audiences));

        ExternalIdentity? identity = await Verifier().VerifyAsync(token, CancellationToken.None);

        identity.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_ExpiredWithinClockSkew_ReturnsIdentity()
    {
        string token = _keys.Sign(Claims(expires: Now.AddMinutes(-1)));

        ExternalIdentity? identity = await Verifier().VerifyAsync(token, CancellationToken.None);

        identity.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_ExpiredBeyondClockSkew_ReturnsNull()
    {
        string token = _keys.Sign(Claims(expires: Now.AddMinutes(-3)));

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_NotBeforeInTheFuture_ReturnsNull()
    {
        JsonObject claims = Claims();
        claims["nbf"] = Now.AddHours(1).ToUnixTimeSeconds();
        string token = _keys.Sign(claims);

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_AnotherClientId_ReturnsNull()
    {
        string token = _keys.Sign(Claims(audience: "someone-else.apps.googleusercontent.com"));

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    public async Task VerifyAsync_AnotherIssuer_ReturnsNull(string issuer)
    {
        string token = _keys.Sign(Claims(issuer: issuer));

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_PayloadChangedAfterSigning_ReturnsNull()
    {
        string token = _keys.Sign(Claims(subject: "victim"));
        string[] parts = token.Split('.');
        string forged = TokenFactory.Unsigned(Claims(subject: "attacker")).Split('.')[1];

        (await Verifier().VerifyAsync(parts[0] + "." + forged + "." + parts[2], CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_AlgorithmNone_ReturnsNull()
    {
        string token = TokenFactory.Unsigned(Claims());

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_HmacAlgorithm_ReturnsNull()
    {
        string token = TokenFactory.HmacSigned(Claims());

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_KeyIdNotPublished_ReturnsNull()
    {
        string token = _keys.Sign(Claims(), kid: "unknown-key");

        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_ManyTokens_FetchesTheKeySetOnce()
    {
        GoogleTokenVerifier verifier = Verifier();

        _ = await verifier.VerifyAsync(_keys.Sign(Claims(subject: "a")), CancellationToken.None);
        _ = await verifier.VerifyAsync(_keys.Sign(Claims(subject: "b")), CancellationToken.None);

        _keySet.Requests.Should().Be(1);
    }

    [Fact]
    public async Task VerifyAsync_UnknownKeyIdAgainWithinAMinute_DoesNotFetchAgain()
    {
        GoogleTokenVerifier verifier = Verifier();
        string forged = _keys.Sign(Claims(), kid: "unknown-key");

        _ = await verifier.VerifyAsync(forged, CancellationToken.None);
        _ = await verifier.VerifyAsync(forged, CancellationToken.None);

        _keySet.Requests.Should().Be(1, "a forged key id must not make the server fetch on every request");
    }

    [Fact]
    public async Task VerifyAsync_KeySetOlderThanAnHour_FetchesAgain()
    {
        GoogleTokenVerifier verifier = Verifier();
        _ = await verifier.VerifyAsync(_keys.Sign(Claims()), CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        _ = await verifier.VerifyAsync(_keys.Sign(Claims(expires: _clock.GetUtcNow().AddMinutes(5))), CancellationToken.None);

        _keySet.Requests.Should().Be(2);
    }

    [Fact]
    public async Task VerifyAsync_KeySetUnavailable_ReturnsNullInsteadOfTrusting()
    {
        _keySet.Status = HttpStatusCode.InternalServerError;

        (await Verifier().VerifyAsync(_keys.Sign(Claims()), CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("!!.!!.!!")]
    public async Task VerifyAsync_MalformedToken_ReturnsNull(string token)
    {
        (await Verifier().VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_ClientIdNotConfigured_RefusesWithoutFetchingKeys()
    {
        string token = _keys.Sign(Claims());

        (await Verifier(clientId: string.Empty).VerifyAsync(token, CancellationToken.None)).Should().BeNull();
        _keySet.Requests.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAsync_NoSubject_ReturnsNull()
    {
        JsonObject claims = Claims();
        _ = claims.Remove("sub");

        (await Verifier().VerifyAsync(_keys.Sign(claims), CancellationToken.None)).Should().BeNull();
    }

    private GoogleTokenVerifier Verifier(string clientId = ClientId)
        => new(new ExternalSignInOptions { GoogleClientId = clientId }, _http, _clock);

    private static JsonObject Claims(
        string issuer = Issuer,
        JsonNode? audience = null,
        string subject = "109876543210",
        string email = "player@example.test",
        DateTimeOffset? expires = null)
        => new()
        {
            ["iss"] = issuer,
            ["aud"] = audience ?? JsonValue.Create(ClientId),
            ["sub"] = subject,
            ["email"] = email,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = (expires ?? Now.AddHours(1)).ToUnixTimeSeconds(),
        };
}
