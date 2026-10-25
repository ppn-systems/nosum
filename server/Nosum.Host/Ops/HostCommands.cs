// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Domain.Accounts;
using Nosum.Domain.Admin;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Persistence;

namespace Nosum.Host.Ops;

/// <summary>
/// The host's command line (docs/adr/ADR-031 D7, D3): <c>admin grant|revoke|list</c> changes the admin role straight in the database - the only way a
/// role changes, never a packet - and <c>healthcheck</c> asks the running server's <c>/readyz</c>, so the container needs no curl. Every role change is
/// audited. Output is plain text for an operator, not log events.
/// </summary>
internal static class HostCommands
{
    /// <summary>The usage text.</summary>
    internal const string Usage = "usage: Nosum.Host [admin grant <nickname> | admin revoke <nickname> | admin list | healthcheck]";

    /// <summary>Runs a command.</summary>
    /// <param name="args">The command line, never empty.</param>
    /// <param name="database">Options of the database (admin commands only).</param>
    /// <param name="opsPort">Port of the running server's ops endpoint (healthcheck only).</param>
    /// <param name="output">Where the answer goes.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The process exit code: 0 on success.</returns>
    internal static async Task<int> RunAsync(string[] args, DbContextOptions<NosumDbContext> database, ushort opsPort, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(output);

        if (args.Length == 1 && args[0] == "healthcheck")
        {
            return await HealthCheckAsync(opsPort, output, cancellationToken).ConfigureAwait(false);
        }

        if (args.Length >= 2 && args[0] == "admin")
        {
            return await AdminAsync(args, database, output, cancellationToken).ConfigureAwait(false);
        }

        await output.WriteLineAsync(Usage).ConfigureAwait(false);
        return 2;
    }

    private static async Task<int> HealthCheckAsync(ushort port, TextWriter output, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/readyz"), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync(response.IsSuccessStatusCode ? "ready" : "not ready").ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            await output.WriteLineAsync("not reachable").ConfigureAwait(false);
            return 1;
        }
        catch (TaskCanceledException)
        {
            await output.WriteLineAsync("timed out").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> AdminAsync(string[] args, DbContextOptions<NosumDbContext> database, TextWriter output, CancellationToken cancellationToken)
    {
        NosumDbContext db = new(database);
        await using (db.ConfigureAwait(false))
        {
            if (args[1] == "list" && args.Length == 2)
            {
                foreach (string nickname in await db.Players.Where(p => p.Role == PlayerRole.Admin).OrderBy(p => p.Nickname).Select(p => p.Nickname).ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    await output.WriteLineAsync(nickname).ConfigureAwait(false);
                }

                return 0;
            }

            if (args.Length != 3 || (args[1] != "grant" && args[1] != "revoke"))
            {
                await output.WriteLineAsync(Usage).ConfigureAwait(false);
                return 2;
            }

            bool grant = args[1] == "grant";
            string key = NicknameRules.Key(NicknameRules.Normalize(args[2]));
            Player? player = await db.Players.FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                await output.WriteLineAsync("no player has that nickname").ConfigureAwait(false);
                return 1;
            }

            if (grant && player.IsGuest)
            {
                await output.WriteLineAsync("a guest cannot be an admin: register the account first").ConfigureAwait(false);
                return 1;
            }

            PlayerRole wanted = grant ? PlayerRole.Admin : PlayerRole.Player;
            if (player.Role != wanted)
            {
                player.Role = wanted;
                _ = db.AdminAudit.Add(new AdminAuditEntry
                {
                    Action = (byte)(grant ? AdminAction.GrantRole : AdminAction.RevokeRole),
                    TargetId = player.Id,
                    Detail = "host command line",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                _ = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{player.Nickname} is {(grant ? "an admin" : "no longer an admin")}; a connection that is already signed in keeps its level until it signs in again, but every admin action re-checks the role")).ConfigureAwait(false);
            return 0;
        }
    }
}
