// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>What the server costs the database per client packet (docs/adr/ADR-035, docs/perf/db-round-trips.md).</summary>
[Collection(NalixHostSerial.Name)]
public sealed class DbBudgetTests : IClassFixture<DbBudgetRun>
{
    private readonly DbBudgetRun _run;

    public DbBudgetTests(DbBudgetRun run) => _run = run;

    [Fact]
    public void Run_Dump_PrintsEveryRow()
    {
        string report = string.Join(Environment.NewLine, _run.Results.Select(r => $"{r.Group,-12} {r.Name,-18} {r.Variant,-44} reads={r.Counts.Reads} writes={r.Counts.Writes} tx={r.Counts.Transactions} saves={r.Counts.SaveChanges} repeated={r.Counts.Repeated}"));
        string? path = Environment.GetEnvironmentVariable("NOSUM_DB_BUDGET_DUMP");
        if (path is not null)
        {
            File.WriteAllText(path, report + Environment.NewLine + string.Join(Environment.NewLine, _run.Failures));
        }

        _run.Failures.Should().BeEmpty();
    }
}
