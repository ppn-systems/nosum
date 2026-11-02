// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Wasm.Socket.Coop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The co-op channel reports an invitation only when the server accepted it (critique-social finding 7).</summary>
public sealed class CoopInviteSentTests
{
    [Fact]
    public async Task Invite_ServerAccepts_RaisesInviteSentWithThePlayer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith<CoopResult>(_ => new CoopResult { Status = CoopStatus.Ok });
        using CoopChannel coop = new(socket);
        TaskCompletionSource<uint> sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        coop.InviteSent += id => sent.TrySetResult(id);

        coop.Invite(7);

        (await sent.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(7u);
    }

    [Fact]
    public async Task Invite_ServerRefuses_DoesNotRaiseInviteSent()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith<CoopResult>(_ => new CoopResult { Status = CoopStatus.TargetInCoop });
        socket.ReplyWith<CoopSnapshot>(_ => new CoopSnapshot());
        using CoopChannel coop = new(socket);
        TaskCompletionSource<CoopStatus> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool raised = false;
        coop.InviteSent += _ => raised = true;
        coop.Completed += status => completed.TrySetResult(status);

        coop.Invite(7);

        (await completed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(CoopStatus.TargetInCoop);
        raised.Should().BeFalse();
    }
}
