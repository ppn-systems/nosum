// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Community;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Player;
using Nosum.Host.Ops;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Buildings;
using Nosum.Infrastructure.Caching;
using Nosum.Infrastructure.Combat;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Persistence;

namespace Nosum.Host;

internal static partial class Program
{
    // Everything after the ops endpoint is up: migrate, register, serve until the shutdown token fires.
    private static async Task<int> RunServerAsync(DbContextOptions<NosumDbContext> database, CancellationTokenSource shutdown)
    {
        await NosumDatabase.MigrateAsync(database, shutdown.Token).ConfigureAwait(false);
        HostServices.Register(database);

        // Idle guests (30 days without coming back) are deleted once an hour.
        Task guestCleanup = new GuestCleanupService(database, TimeProvider.System).RunAsync(
            GuestCleanupService.DefaultInterval,
            ex => Console.Error.WriteLine("Guest cleanup failed: " + ex.Message),
            shutdown.Token);

        ushort wsPort = WsPort();
        NetworkApplication app = BuildApplication(wsPort, null, IsDevelopment());
        await OpsServices.LoadMutesAsync(shutdown.Token).ConfigureAwait(false);

        // Goals of seasons that ended while the server was down expire now, and rewards a crash left unpaid are paid (docs/adr/ADR-030 D3).
        ICommunityGoalService goals = InstanceManager.Instance.GetExistingInstance<ICommunityGoalService>()
            ?? throw new InvalidOperationException("The community goal service is not registered.");
        try
        {
            _ = await goals.CloseEndedSeasonsAsync(shutdown.Token).ConfigureAwait(false);
            _ = await goals.SettlePendingAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Community goal start-up sweep failed: " + ex.Message).ConfigureAwait(false);
        }

        // Listings past their expiry give their items back to the sellers (docs/adr/ADR-013).
        IMarketService market = InstanceManager.Instance.GetExistingInstance<IMarketService>()
            ?? throw new InvalidOperationException("The market service is not registered.");
        Task marketExpiry = new MarketExpiryService(market, TimeProvider.System).RunAsync(
            MarketExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Market expiry failed: " + ex.Message),
            shutdown.Token);

        // Items that lay on a shared map for ten minutes vanish (docs/adr/ADR-021 D6).
        IGroundItems groundItems = InstanceManager.Instance.GetExistingInstance<IGroundItems>()
            ?? throw new InvalidOperationException("The ground item service is not registered.");
        Task groundItemExpiry = new GroundItemExpiryService(groundItems, TimeProvider.System).RunAsync(
            GroundItemExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Ground item expiry failed: " + ex.Message),
            shutdown.Token);

        // Animals inside a building with a player in it pick a new walk every few seconds (docs/adr/ADR-028 D4).
        AnimalWanderService wander = InstanceManager.Instance.GetExistingInstance<AnimalWanderService>()
            ?? throw new InvalidOperationException("The animal wander service is not registered.");
        Task animalWander = wander.RunAsync(
            AnimalWanderService.DefaultInterval,
            ex => Console.Error.WriteLine("Animal wander failed: " + ex.Message),
            shutdown.Token);

        // Mail past its 30 days is removed with its attachment (docs/adr/ADR-029 D5).
        IMailService mail = InstanceManager.Instance.GetExistingInstance<IMailService>()
            ?? throw new InvalidOperationException("The mail service is not registered.");
        Task mailExpiry = new MailExpiryService(mail, TimeProvider.System).RunAsync(
            MailExpiryService.DefaultInterval,
            ex => Console.Error.WriteLine("Mail expiry failed: " + ex.Message),
            shutdown.Token);

        // Monsters roam and aggro towards players (docs/adr/ADR-046 D1).
        ICombatService combatService = InstanceManager.Instance.GetExistingInstance<ICombatService>()
            ?? throw new InvalidOperationException("The combat service is not registered.");
        Task monsterAi = combatService.RunAiAsync(
            CombatService.DefaultAiInterval,
            ex => Console.Error.WriteLine("Monster AI failed: " + ex.Message),
            shutdown.Token);

        if (LoadTestMode())
        {
            OpsLog.Warning("host.load_test_mode", "NOSUM_LOAD_TEST is on: the per-address rate limits are OFF. Never run players on this server.");
        }

        OpsLog.Information("host.started", "The server is accepting connections.", new LogField("ws_port", (int)wsPort));
        Task keyAnnouncement = ServerKey.AnnounceAsync(Console.WriteLine, shutdown.Token);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
            if (InstanceManager.Instance.GetExistingInstance<IPlayerService>() is { } activePlayers)
            {
                await activePlayers.FlushPositionsAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        // Whatever waits for its write goes to the database before the process ends (docs/adr/ADR-035 D7).
        await (InstanceManager.Instance.GetExistingInstance<WriteBehind>() ?? throw new InvalidOperationException("The write-behind queue is not registered.")).FlushAllAsync(CancellationToken.None).ConfigureAwait(false);

        await keyAnnouncement.ConfigureAwait(false);
        await animalWander.ConfigureAwait(false);
        await monsterAi.ConfigureAwait(false);

        await guestCleanup.ConfigureAwait(false);
        await marketExpiry.ConfigureAwait(false);
        await groundItemExpiry.ConfigureAwait(false);
        await mailExpiry.ConfigureAwait(false);

        return 0;
    }
}
