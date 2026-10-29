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

/// <summary>
/// Microsoft ID tokens are accepted only when signed by the tenant's keys, for this application id, from the issuer of their own tenant, and in time.
/// The subject is the object id (docs/adr/ADR-069 D3).
/// </summary>
public sealed class MicrosoftTokenVerifierTests : IDisposable
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";
    private const string Tenant = "9b0c3c9e-2d1f-4a6b-8c7d-1e2f3a4b5c6d";
    private const string OtherTenant = "0a1b2c3d-4e5f-4a7b-8c9d-0e1f2a3b4c5d";

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly TokenFactory _keys = new();
    private readonly StubKeySetHandler _keySet;
    private readonly HttpClient _http;
    private readonly MutableClock _clock = new(Now);

    public MicrosoftTokenVerifierTests()
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
    public async Task VerifyAsync_ValidToken_UsesTheObjectIdAsSubject()
    {
        string token = _keys.Sign(Claims(objectId: "aaaaaaaa-0000-0000-0000-000000000001", subject: "pairwise-sub", email: "player@example.test"));

        ExternalIdentity? identity = await Verifier(Tenant).VerifyAsync(token, CancellationToken.None);

        identity.Should().Be(new ExternalIdentity(ExternalProvider.Microsoft, "aaaaaaaa-0000-0000-0000-000000000001", "player@example.test"));
    }

    [Fact]
    public async Task VerifyAsync_NoObjectId_FallsBackToTheSubject()
    {
        JsonObject claims = Claims();
        _ = claims.Remove("oid");

        ExternalIdentity? identity = await Verifier(Tenant).VerifyAsync(_keys.Sign(claims), CancellationToken.None);

        identity!.Value.Subject.Should().Be("pairwise-sub");
    }

    [Fact]
    public async Task VerifyAsync_CommonTenant_AcceptsAnyTenantWithItsOwnIssuer()
    {
        string token = _keys.Sign(Claims(tenantId: OtherTenant));

        ExternalIdentity? identity = await Verifier("common").VerifyAsync(token, CancellationToken.None);

        identity.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_TokenFromAnotherTenantThanTheConfiguredOne_ReturnsNull()
    {
        string token = _keys.Sign(Claims(tenantId: OtherTenant));

        (await Verifier(Tenant).VerifyAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_IssuerNamesAnotherTenant_ReturnsNull()
    {
        JsonObject claims = Claims(tenantId: Tenant);
        claims["iss"] = "https://login.microsoftonline.com/" + OtherTenant + "/v2.0";

        (await Verifier("common").VerifyAsync(_keys.Sign(claims), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_GoogleIssuer_ReturnsNull()
    {
        JsonObject claims = Claims();
        claims["iss"] = "https://accounts.google.com";

        (await Verifier("common").VerifyAsync(_keys.Sign(claims), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_AnotherApplicationId_ReturnsNull()
    {
        JsonObject claims = Claims();
        claims["aud"] = "99999999-8888-7777-6666-555555555555";

        (await Verifier("common").VerifyAsync(_keys.Sign(claims), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_Expired_ReturnsNull()
    {
        JsonObject claims = Claims();
        claims["exp"] = Now.AddMinutes(-10).ToUnixTimeSeconds();

        (await Verifier("common").VerifyAsync(_keys.Sign(claims), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_AlgorithmNone_ReturnsNull()
    {
        (await Verifier("common").VerifyAsync(TokenFactory.Unsigned(Claims()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_HmacAlgorithm_ReturnsNull()
    {
        (await Verifier("common").VerifyAsync(TokenFactory.HmacSigned(Claims()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_KeySetUnavailable_ReturnsNull()
    {
        _keySet.Status = HttpStatusCode.ServiceUnavailable;

        (await Verifier("common").VerifyAsync(_keys.Sign(Claims()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_ClientIdNotConfigured_RefusesWithoutFetchingKeys()
    {
        (await Verifier("common", clientId: string.Empty).VerifyAsync(_keys.Sign(Claims()), CancellationToken.None)).Should().BeNull();
        _keySet.Requests.Should().Be(0);
    }

    private MicrosoftTokenVerifier Verifier(string tenant, string clientId = ClientId)
        => new(new ExternalSignInOptions { MicrosoftClientId = clientId, MicrosoftTenant = tenant }, _http, _clock);

    private static JsonObject Claims(string tenantId = Tenant, string objectId = "aaaaaaaa-0000-0000-0000-000000000001", string subject = "pairwise-sub", string email = "player@example.test")
        => new()
        {
            ["iss"] = "https://login.microsoftonline.com/" + tenantId + "/v2.0",
            ["aud"] = ClientId,
            ["tid"] = tenantId,
            ["oid"] = objectId,
            ["sub"] = subject,
            ["email"] = email,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.AddHours(1).ToUnixTimeSeconds(),
        };
}
