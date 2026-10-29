// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.UnitTests.Server.Identity;

/// <summary>
/// Signs ID tokens with a locally generated RSA key and publishes its key set, so the verifiers run with no network and no provider
/// (docs/adr/ADR-069). Signatures from other keys, unsigned tokens and HMAC tokens are built here too, to be refused.
/// </summary>
internal sealed class TokenFactory : IDisposable
{
    internal const string Kid = "test-key-1";

    private readonly RSA _key = RSA.Create(2048);

    /// <summary>The key set a provider would publish for this key.</summary>
    /// <returns>A JWKS document as JSON.</returns>
    internal string Jwks()
    {
        RSAParameters parameters = _key.ExportParameters(false);
        JsonObject jwk = new()
        {
            ["kty"] = "RSA",
            ["kid"] = Kid,
            ["use"] = "sig",
            ["alg"] = "RS256",
            ["n"] = Base64Url(parameters.Modulus!),
            ["e"] = Base64Url(parameters.Exponent!),
        };
        return new JsonObject { ["keys"] = new JsonArray(jwk) }.ToJsonString();
    }

    /// <summary>Signs claims as RS256 under a key id.</summary>
    /// <param name="claims">The payload.</param>
    /// <param name="kid">The key id in the header.</param>
    /// <returns>The compact token.</returns>
    internal string Sign(JsonObject claims, string kid = Kid)
    {
        string input = Segment(new JsonObject { ["alg"] = "RS256", ["kid"] = kid, ["typ"] = "JWT" }) + "." + Segment(claims);
        byte[] signature = _key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + Base64Url(signature);
    }

    /// <summary>Builds a token whose header says <c>alg: none</c> and which has no signature.</summary>
    /// <param name="claims">The payload.</param>
    /// <returns>The compact token.</returns>
    internal static string Unsigned(JsonObject claims)
        => Segment(new JsonObject { ["alg"] = "none", ["typ"] = "JWT" }) + "." + Segment(claims) + ".";

    /// <summary>Builds a token signed with HMAC-SHA256, the algorithm a verifier must refuse for a public-key provider.</summary>
    /// <param name="claims">The payload.</param>
    /// <returns>The compact token.</returns>
    internal static string HmacSigned(JsonObject claims)
    {
        string input = Segment(new JsonObject { ["alg"] = "HS256", ["kid"] = Kid, ["typ"] = "JWT" }) + "." + Segment(claims);
        byte[] signature = HMACSHA256.HashData(Encoding.ASCII.GetBytes("not-a-public-key"), Encoding.ASCII.GetBytes(input));
        return input + "." + Base64Url(signature);
    }

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();

    private static string Segment(JsonObject json) => Base64Url(Encoding.UTF8.GetBytes(json.ToJsonString()));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Answers every request with one body and status, and counts them: the provider's key set.</summary>
internal sealed class StubKeySetHandler(string body) : HttpMessageHandler
{
    /// <summary>Gets how many key-set requests were made.</summary>
    public int Requests { get; private set; }

    /// <summary>Gets or sets the status the stub answers with.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Gets or sets the body the stub answers with.</summary>
    public string Body { get; set; } = body;

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
