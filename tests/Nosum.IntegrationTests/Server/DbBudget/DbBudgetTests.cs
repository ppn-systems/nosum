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
        string report = string.Join(Environment.NewLine, _run.Results.Select(r => string.Join('\t', r.Group, r.Name, r.Variant, r.Outcome, r.Counts.Reads, r.Counts.Writes, r.Counts.Transactions, r.Counts.SaveChanges, r.Counts.Repeated)));
        string? path = Environment.GetEnvironmentVariable("NOSUM_DB_BUDGET_DUMP");
        if (path is not null)
        {
            File.WriteAllText(path, report + Environment.NewLine + string.Join(Environment.NewLine, _run.Failures) + Environment.NewLine + "MISSING: " + string.Join(", ", this.Missing()));
        }

        string? table = Environment.GetEnvironmentVariable("NOSUM_DB_BUDGET_TABLE");
        if (table is not null)
        {
            File.WriteAllText(table, this.MarkdownTable());
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

    // The rows, one per packet and case (the same case measured twice keeps the dearer run), in opcode order; the doc table is this output.
    private string MarkdownTable()
    {
        System.Text.StringBuilder text = new();
        text.AppendLine("| Opcode | Packet / event | Case | Reply | Reads | Writes | Tx | SaveChanges | Repeated |");
        text.AppendLine("|:--|:--|:--|:--|--:|--:|--:|--:|--:|");
        IEnumerable<IGrouping<string, Measurement>> groups = _run.Results
            .GroupBy(r => r.Group + "|" + r.Opcode + "|" + r.Key)
            .OrderBy(g => g.First().Opcode ?? ushort.MaxValue).ThenBy(g => g.First().Group, StringComparer.Ordinal);
        foreach (IGrouping<string, Measurement> g in groups)
        {
            Measurement first = g.First();
            string opcode = first.Opcode is { } o ? "0x" + o.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) : "-";
            if (first.Skipped is not null)
            {
                text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {opcode} | {first.Name} | not measured | - | - | - | - | - | {first.Skipped} |");
                continue;
            }

            int reads = g.Max(r => r.Counts.Reads);
            int writes = g.Max(r => r.Counts.Writes);
            int tx = g.Max(r => r.Counts.Transactions);
            int saves = g.Max(r => r.Counts.SaveChanges);
            int repeated = g.Max(r => r.Counts.Repeated);
            text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {opcode} | {first.Name} | {first.Variant} | {first.Outcome} | {reads} | {writes} | {tx} | {saves} | {repeated} |");
        }

        return text.ToString();
    }

    [Fact]
    public void HotPaths_NeverCostMoreThanTheirBaseline()
    {
        List<string> over = [];
        foreach ((string key, DbBudgetBaseline.Limit limit) in DbBudgetBaseline.HotPaths)
        {
            List<Measurement> rows = [.. _run.Results.Where(r => r.Key == key && r.Skipped is null)];
            if (rows.Count == 0)
            {
                over.Add(key + ": not measured (the scenario failed or the row was renamed)");
                continue;
            }

            foreach (Measurement row in rows)
            {
                DbCounts c = row.Counts;
                if (c.Reads > limit.Reads || c.Writes > limit.Writes || c.Transactions > limit.Transactions || c.SaveChanges > limit.SaveChanges)
                {
                    over.Add($"{key}: reads {c.Reads}/{limit.Reads}, writes {c.Writes}/{limit.Writes}, transactions {c.Transactions}/{limit.Transactions}, SaveChanges {c.SaveChanges}/{limit.SaveChanges}");
                }
            }
        }

        over.Should().BeEmpty("a hot-path packet got dearer than its recorded baseline (docs/adr/ADR-035); find what added the database call");
    }

    private IEnumerable<string> Missing()
    {
        HashSet<ushort> seen = [.. _run.Results.Where(r => r.Opcode is not null).Select(r => r.Opcode!.Value)];
        return ClientOpcodes().Where(o => !seen.Contains(o.Key)).Select(o => o.Value + " (0x" + o.Key.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + ")");
    }
}
