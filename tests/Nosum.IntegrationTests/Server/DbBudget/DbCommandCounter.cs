// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// Test-only EF Core interceptor that counts what the server asks PostgreSQL for: every executed command, every transaction
/// and every <c>SaveChanges</c>. It is never registered by the production host (docs/adr/ADR-035).
/// </summary>
/// <remarks>
/// A command is one wire round trip (EF batches the statements of one <c>SaveChanges</c> into one command); an explicit
/// transaction adds a <c>BEGIN</c> and a <c>COMMIT</c> round trip of its own, which Npgsql sends below the command interceptors.
/// </remarks>
internal sealed class DbCommandCounter : DbCommandInterceptor, IDbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];

    /// <summary>Gets how many events were recorded so far; a mark for <see cref="Since"/>.</summary>
    public int Mark
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Summarizes everything recorded after <paramref name="mark"/>.</summary>
    /// <param name="mark">A value read from <see cref="Mark"/>.</param>
    /// <returns>The counts of that window.</returns>
    public DbCounts Since(int mark)
    {
        Entry[] window;
        lock (_gate)
        {
            window = [.. _entries.Skip(mark)];
        }

        int reads = 0;
        int writes = 0;
        int transactions = 0;
        int saves = 0;
        Dictionary<string, int> perStatement = [];
        foreach (Entry entry in window)
        {
            switch (entry.Kind)
            {
                case EntryKind.Read:
                    reads++;
                    break;
                case EntryKind.Write:
                    writes++;
                    break;
                case EntryKind.Transaction:
                    transactions++;
                    break;
                case EntryKind.Save:
                    saves++;
                    break;
            }

            if (entry.Kind is EntryKind.Read or EntryKind.Write)
            {
                perStatement[entry.Text] = perStatement.GetValueOrDefault(entry.Text) + 1;
            }
        }

        return new DbCounts(reads, writes, transactions, saves, perStatement);
    }

    /// <summary>Waits until no command was recorded for a short while, so work a request leaves running after its reply is counted.</summary>
    /// <param name="quiet">How long the counter must stay unchanged.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A task that completes when the database went quiet (or after a generous upper bound).</returns>
    public async Task SettleAsync(TimeSpan quiet, CancellationToken ct)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(10);
        int seen = this.Mark;
        DateTime since = DateTime.UtcNow;
        while (DateTime.UtcNow < limit)
        {
            await Task.Delay(20, ct);
            int now = this.Mark;
            if (now != seen)
            {
                seen = now;
                since = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - since >= quiet)
            {
                return;
            }
        }
    }

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        this.Record(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        this.Record(command);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        this.Record(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        this.Record(command);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc/>
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        this.Record(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        this.Record(command);
        return new ValueTask<InterceptionResult<object>>(result);
    }

    /// <inheritdoc/>
    public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        this.Add(EntryKind.Transaction, "BEGIN");
        return result;
    }

    /// <inheritdoc/>
    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        this.Add(EntryKind.Transaction, "BEGIN");
        return new ValueTask<InterceptionResult<DbTransaction>>(result);
    }

    /// <inheritdoc/>
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        this.Add(EntryKind.Save, "SaveChanges");
        return result;
    }

    /// <inheritdoc/>
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        this.Add(EntryKind.Save, "SaveChanges");
        return new ValueTask<InterceptionResult<int>>(result);
    }

    private void Record(DbCommand command)
    {
        string text = command.CommandText.TrimStart();
        bool read = text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        this.Add(read ? EntryKind.Read : EntryKind.Write, Normalize(text));
    }

    // The statement with parameter values already out of it (EF sends them as @p0, @__id_0 ...); long statements are cut so the
    // per-statement tally in a report stays readable. Two commands with the same text are "the same lookup".
    private static string Normalize(string text)
    {
        string oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 160 ? oneLine : oneLine[..160];
    }

    private void Add(EntryKind kind, string text)
    {
        lock (_gate)
        {
            _entries.Add(new Entry(kind, text));
        }
    }

    private enum EntryKind
    {
        Read,
        Write,
        Transaction,
        Save,
    }

    private readonly record struct Entry(EntryKind Kind, string Text);
}

/// <summary>What one measured window cost the database.</summary>
/// <param name="Reads">Executed <c>SELECT</c> commands.</param>
/// <param name="Writes">Executed <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c> commands.</param>
/// <param name="Transactions">Explicit transactions started (each adds a <c>BEGIN</c> and a <c>COMMIT</c> round trip).</param>
/// <param name="SaveChanges"><c>SaveChanges</c> calls, including ones that found nothing to write.</param>
/// <param name="Statements">How often each normalized statement ran in the window.</param>
internal sealed record DbCounts(int Reads, int Writes, int Transactions, int SaveChanges, IReadOnlyDictionary<string, int> Statements)
{
    /// <summary>Gets the executed commands, reads plus writes.</summary>
    public int Commands => this.Reads + this.Writes;

    /// <summary>Gets how many commands repeated a statement already run in the same window (a repeated lookup or an N+1).</summary>
    public int Repeated => this.Statements.Values.Sum(n => n - 1);

    /// <summary>Gets the zero cost.</summary>
    public static DbCounts None { get; } = new(0, 0, 0, 0, new Dictionary<string, int>());
}
