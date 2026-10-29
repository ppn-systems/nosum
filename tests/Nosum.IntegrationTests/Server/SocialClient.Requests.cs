// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;

namespace Nosum.IntegrationTests.Server;

/// <summary>The requests snapshot as a client received it.</summary>
internal sealed record RequestsCopy(long ServerNowMs, int Day, long DayEndsAtMs, long EarlyUntilMs, RequestRow[] Rows);

/// <summary>The requests board requests of the wire tests (docs/adr/ADR-036).</summary>
internal sealed partial class SocialClient
{
    public async Task<RequestsCopy> GetRequestsAsync(CancellationToken ct)
    {
        using GetRequests request = new();
        RequestsSnapshot reply = await this.RequestAsync<RequestsSnapshot>(request, ct);
        return new RequestsCopy(
            reply.ServerNowMs,
            reply.Day,
            reply.DayEndsAtMs,
            reply.EarlyUntilMs,
            [.. reply.Rows.Select(row => new RequestRow { Slot = row.Slot, ItemId = row.ItemId, Quantity = row.Quantity, RewardCoins = row.RewardCoins, Filled = row.Filled })]);
    }

    public async Task<(RequestStatus Status, long Reward, bool Bonus)> DeliverRequestAsync(int day, int slot, CancellationToken ct)
    {
        using DeliverRequest request = new() { Day = day, Slot = slot };
        RequestResult result = await this.RequestAsync<RequestResult>(request, ct);
        return (result.Status, result.Reward, result.Bonus);
    }
}
