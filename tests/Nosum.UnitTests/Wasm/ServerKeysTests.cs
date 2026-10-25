// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Security;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class ServerKeysTests
{
    private static readonly string s_a = new('a', 64);
    private static readonly string s_b = new('B', 64);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void TryNormalize_NotA64DigitHexKey_ReturnsFalse(string text)
        => ServerKeys.TryNormalize(text, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_HexInAnyCaseWithWhiteSpace_ReturnsUpperCase()
    {
        ServerKeys.TryNormalize("  " + s_a + "\n", out string key).Should().BeTrue();
        key.Should().Be(new string('A', 64));
        ServerKeys.AreSame(s_a, new string('A', 64)).Should().BeTrue();
        ServerKeys.AreSame(s_a, s_b).Should().BeFalse();
        ServerKeys.AreSame(null, s_a).Should().BeFalse();
    }

    [Fact]
    public void FromConfiguration_ReadsUpToTwoKeysSkippingBlanks()
    {
        Dictionary<string, string> config = new() { ["Server:PublicKeys:1"] = s_b };

        ServerKeys.FromConfiguration(k => config.GetValueOrDefault(k)).Should().Equal(new string('B', 64));
        ServerKeys.FromConfiguration(_ => null).Should().BeEmpty();
    }

    [Fact]
    public void FromConfiguration_MalformedOrTooManyKeys_StopsInsteadOfWeakeningThePin()
    {
        Func<string[]> malformed = () => ServerKeys.FromConfiguration(k => k.EndsWith('0') ? "oops" : null);
        Func<string[]> tooMany = () => ServerKeys.FromConfiguration(_ => s_a);

        _ = malformed.Should().Throw<FormatException>();
        _ = tooMany.Should().Throw<FormatException>();
    }

    [Fact]
    public void ApplyBootConfiguration_WithKeys_PinsThemAndSkipsTheTofuStore()
    {
        bool storeCreated = false;
        Dictionary<string, string> config = new() { ["Server:PublicKeys:0"] = s_a, ["Server:RequirePin"] = "true" };

        GameSocketOptions options = new GameSocketOptions().ApplyBootConfiguration(k => config.GetValueOrDefault(k), () =>
        {
            storeCreated = true;
            return new NoStore();
        });

        options.ServerPublicKeys.Should().Equal(new string('A', 64));
        options.RequirePin.Should().BeTrue();
        options.KeyStore.Should().BeNull();
        storeCreated.Should().BeFalse();
    }

    [Fact]
    public void ApplyBootConfiguration_WithoutKeys_UsesTheTofuStore()
    {
        GameSocketOptions options = new GameSocketOptions().ApplyBootConfiguration(_ => null, () => new NoStore());

        options.ServerPublicKeys.Should().BeEmpty();
        options.RequirePin.Should().BeFalse();
        options.KeyStore.Should().BeOfType<NoStore>();
    }

    private sealed class NoStore : IServerKeyStore
    {
        public System.Threading.Tasks.ValueTask<string?> GetAsync() => System.Threading.Tasks.ValueTask.FromResult<string?>(null);

        public System.Threading.Tasks.ValueTask SetAsync(string keyHex) => System.Threading.Tasks.ValueTask.CompletedTask;
    }
}
