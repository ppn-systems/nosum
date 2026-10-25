// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Wasm.Socket;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class ConnectionStatusViewModelTests
{
    [Theory]
    [InlineData("en", GameSocketState.Ready, "Online")]
    [InlineData("en", GameSocketState.Reconnecting, "Connection lost, retrying…")]
    [InlineData("vi", GameSocketState.Ready, "Trực tuyến")]
    [InlineData("vi", GameSocketState.Offline, "Ngoại tuyến")]
    public void Text_ForStateAndCulture_ComesFromTheResources(string culture, GameSocketState state, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            FakeSocket socket = new() { State = state };
            ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            using ConnectionStatusViewModel vm = new(socket, new StringLocalizer<Language>(factory));

            vm.Text.Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed class FakeSocket : IGameSocket
    {
        public event Action<GameSocketState>? StateChanged
        {
            add { }
            remove { }
        }

        public event Action? Ready
        {
            add { }
            remove { }
        }

        public GameSocketState State { get; init; }

        public void Start() => throw new NotSupportedException();

        public Task WaitReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
            where TResponse : class, IPacket, IPacketStaticOpcode => throw new NotSupportedException();

        public ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IDisposable Subscribe<TPacket>(Action<TPacket> handler)
            where TPacket : class, IPacket, IPacketStaticOpcode => throw new NotSupportedException();
    }
}
