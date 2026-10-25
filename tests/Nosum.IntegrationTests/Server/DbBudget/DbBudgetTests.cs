// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>What the server costs the database per client packet (docs/adr/ADR-035, docs/perf/db-round-trips.md).</summary>
[Collection(NalixHostSerial.Name)]
public sealed class DbBudgetTests : IClassFixture<DbBudgetRun>
{
    private readonly DbBudgetRun _run;

    public DbBudgetTests(DbBudgetRun run) => _run = run;

    // Every opcode a handler answers.
    internal static IReadOnlyDictionary<ushort, string> ClientOpcodes()
    {
        Dictionary<ushort, string> found = [];
        foreach (Type type in typeof(Nosum.Handlers.Player.PlayerHandlers).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                CustomAttributeData? attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "PacketOpcodeAttribute");
                if (attribute is not null)
                {
                    ushort opcode = Convert.ToUInt16(attribute.ConstructorArguments[0].Value, System.Globalization.CultureInfo.InvariantCulture);
                    found[opcode] = method.Name.Replace("Handle", string.Empty, StringComparison.Ordinal).Replace("Async", string.Empty, StringComparison.Ordinal);
                }
            }
        }

        return found;
    }

    [Fact]
    public void Run_EveryScenarioCompleted()
    {
        string report = string.Join(Environment.NewLine, _run.Results.Select(r => $"{r.Group,-12} {r.Name,-18} {r.Variant,-44} {r.Outcome,-14} reads={r.Counts.Reads} writes={r.Counts.Writes} tx={r.Counts.Transactions} saves={r.Counts.SaveChanges} repeated={r.Counts.Repeated}"));
        string? path = Environment.GetEnvironmentVariable("NOSUM_DB_BUDGET_DUMP");
        if (path is not null)
        {
            File.WriteAllText(path, report + Environment.NewLine + string.Join(Environment.NewLine, _run.Failures) + Environment.NewLine + "MISSING: " + string.Join(", ", this.Missing()));
        }

        string? detail = Environment.GetEnvironmentVariable("NOSUM_DB_BUDGET_STATEMENTS");
        if (detail is not null)
        {
            File.WriteAllText(detail, string.Join(Environment.NewLine, _run.Results.Where(r => r.Counts.Commands > 0).Select(r => "## " + r.Name + " / " + r.Variant + Environment.NewLine + string.Join(Environment.NewLine, r.Counts.Statements.Select(kv => "  " + kv.Value + "x " + kv.Key)))));
        }

        _run.Failures.Should().BeEmpty();
    }

    [Fact]
    public void Run_EveryClientOpcode_HasARowOrAReason()
    {
        this.Missing().Should().BeEmpty("every client packet is measured, or its row says why not");
    }

    private IEnumerable<string> Missing()
    {
        HashSet<ushort> seen = [.. _run.Results.Where(r => r.Opcode is not null).Select(r => r.Opcode!.Value)];
        return ClientOpcodes().Where(o => !seen.Contains(o.Key)).Select(o => o.Value + " (0x" + o.Key.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ")");
    }
}
