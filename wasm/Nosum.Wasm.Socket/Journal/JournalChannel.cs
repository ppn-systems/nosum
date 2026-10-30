// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Journal.C2S;
using Nosum.Contracts.Journal.S2C;

namespace Nosum.Wasm.Socket.Journal;

/// <summary>
/// The client's copy of the collection journal (the items the farmer has obtained, with counts and first-obtained times). The server keeps
/// the discoveries; this channel asks for them when the journal opens and holds the last answer until the next one.
/// </summary>
public sealed class JournalChannel
{
    private readonly IGameSocket _socket;
    private IReadOnlyList<JournalEntry> _entries = [];
    private int _loadGeneration;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public JournalChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when a journal was read. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the discovered entries of the last read: only the items obtained, so an item missing here is still a silhouette.</summary>
    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>Reads the journal. Call when the journal panel opens.</summary>
    public void Load() => _ = this.LoadAsync();

    [SuppressMessage("Design", "CA1031", Justification = "A failed read is retried when the panel opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        int generation = Interlocked.Increment(ref _loadGeneration);
        try
        {
            using PacketScope<GetJournal> request = PacketFactory<GetJournal>.Acquire();
            JournalReply reply = await _socket.RequestAsync<JournalReply>(request.Value).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            Interlocked.Exchange(ref _entries, reply.Entries);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }
}
