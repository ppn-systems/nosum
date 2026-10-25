// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Social;
using Nosum.Gameplay.Chat;
using Nosum.Gameplay.Players;

namespace Nosum.Infrastructure.Chat;

/// <summary>
/// <see cref="IChatService"/> (docs/adr/ADR-029 D1-D3). Every message is cleaned and masked by <see cref="ChatRules.Prepare"/> before anyone
/// sees it; the sender pays one token of <see cref="ChatRateLimiter"/>; a recipient who blocked the sender is skipped without the sender
/// being told. Nothing is stored except what a report keeps: each player has a short in-memory ring of the last
/// <see cref="ChatRules.ReportContextLines"/> messages they received, copied into the report table when they report someone.
/// </summary>
public sealed class ChatService : IChatService
{
    private static readonly TimeSpan s_ringIdle = TimeSpan.FromMinutes(30);
    private const int RingSweepEvery = 512;

    private readonly IMapPresence _presence;
    private readonly ISessionRegistry _sessions;
    private readonly IPlayerNotifier _notifier;
    private readonly INicknameDirectory _nicknames;
    private readonly IBlockList _blocks;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IChunkMap? _chunks;
    private readonly ConcurrentDictionary<Guid, ChatRateLimiter> _limiters = new();
    private readonly ConcurrentDictionary<Guid, Ring> _rings = new();
    private int _recorded;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who is on which map.</param>
    /// <param name="sessions">Who is online (for whispers and co-op chat).</param>
    /// <param name="notifier">Pushes messages to players.</param>
    /// <param name="nicknames">Resolves nicknames.</param>
    /// <param name="blocks">The block lists.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for the rate rule and message times; tests pass a fake one.</param>
    /// <param name="chunks">Who sees whom (docs/adr/ADR-022), for local chat; <see langword="null"/> sends local chat to the whole instance.</param>
    public ChatService(IMapPresence presence, ISessionRegistry sessions, IPlayerNotifier notifier, INicknameDirectory nicknames, IBlockList blocks, IUnitOfWorkFactory unitOfWork, TimeProvider time, IChunkMap? chunks = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _sessions = sessions;
        _notifier = notifier;
        _nicknames = nicknames;
        _blocks = blocks;
        _unitOfWork = unitOfWork;
        _time = time;
        _chunks = chunks;
    }

    /// <inheritdoc/>
    public async Task<ChatStatus> SendAsync(IConnection connection, Guid senderId, ChatChannel channel, string? target, string? text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Enum.IsDefined(channel))
        {
            return ChatStatus.InvalidChannel;
        }

        string clean = ChatRules.Prepare(text);
        if (clean.Length == 0)
        {
            return ChatStatus.Empty;
        }

        if (!_presence.TryGet(connection, out PlayerPresence? me))
        {
            return ChatStatus.NotOnMap;
        }

        string senderName = await _nicknames.GetAsync(senderId, cancellationToken).ConfigureAwait(false);
        if (channel != ChatChannel.Local && NicknameRules.IsGuestName(senderName, out _))
        {
            return ChatStatus.GuestLocalOnly;
        }

        // Who gets it is decided before the rate token is spent, so a refused whisper costs nothing.
        List<Guid> recipients = [];
        string toName = string.Empty;
        switch (channel)
        {
            case ChatChannel.Local:
                this.CollectLocal(me, recipients);
                break;
            case ChatChannel.Map:
                this.CollectMap(me, recipients);
                break;
            case ChatChannel.Coop:
                if (!await this.CollectCoopAsync(senderId, recipients, cancellationToken).ConfigureAwait(false))
                {
                    return ChatStatus.NotInCoop;
                }

                break;
            default:
                (ChatStatus whisper, Guid? targetId, string name) = await this.ResolveWhisperAsync(senderId, target, cancellationToken).ConfigureAwait(false);
                if (whisper != ChatStatus.Ok)
                {
                    return whisper;
                }

                toName = name;
                recipients.Add(targetId!.Value);
                break;
        }

        if (!_limiters.GetOrAdd(senderId, static _ => new ChatRateLimiter()).TryTake(_time.GetUtcNow().ToUnixTimeMilliseconds()))
        {
            return ChatStatus.RateLimited;
        }

        DateTimeOffset now = _time.GetUtcNow();
        ChatLine line = new(channel, senderId, senderName, toName, clean, now);
        using PacketScope<ChatMessage> lease = PacketFactory<ChatMessage>.Acquire();
        ChatMessage message = lease.Value;
        message.Channel = channel;
        message.FromNickname = senderName;
        message.ToNickname = toName;
        message.Text = clean;
        message.SentAtMs = now.ToUnixTimeMilliseconds();

        // The sender always gets their own line back, so the client has one path for every line it shows.
        HashSet<Guid> delivered = [senderId];
        _ = await _notifier.TrySendAsync(senderId, message, cancellationToken).ConfigureAwait(false);
        this.Record(senderId, line);

        foreach (Guid recipient in recipients.Distinct())
        {
            if (recipient == senderId || !delivered.Add(recipient))
            {
                continue;
            }

            if (await _blocks.HasBlockedAsync(recipient, senderId, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (await _notifier.TrySendAsync(recipient, message, cancellationToken).ConfigureAwait(false))
            {
                this.Record(recipient, line);
            }
        }

        return ChatStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<ChatStatus> ReportAsync(Guid reporterId, string nickname, CancellationToken cancellationToken = default)
    {
        if (!BlockList.TryKey(nickname, out string key))
        {
            return ChatStatus.InvalidNickname;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? reported = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (reported is null)
            {
                return ChatStatus.UnknownPlayer;
            }

            Guid reportedId = reported.Id;
            if (reportedId == reporterId)
            {
                return ChatStatus.SelfBlock;
            }

            List<ChatLine> context = this.Snapshot(reporterId);
            if (!context.Exists(l => l.SenderId == reportedId))
            {
                return ChatStatus.NothingToReport;
            }

            ChatReport report = new() { ReporterId = reporterId, ReportedId = reportedId, CreatedAt = _time.GetUtcNow() };
            unit.Repository<ChatReport>().Add(report);
            int sequence = 0;
            foreach (ChatLine line in context)
            {
                unit.Repository<ChatReportLine>().Add(new ChatReportLine
                {
                    ReportId = report.Id,
                    Sequence = sequence++,
                    Channel = (byte)line.Channel,
                    SenderId = line.SenderId,
                    SenderNickname = line.SenderNickname,
                    Text = line.Text,
                    SentAt = line.At,
                });
            }

            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ChatStatus.Ok;
        }
    }

    // Local chat: the players near the sender. ADR-022 makes this the sender's chunk interest set; until the chunk publisher is on master it is
    // the whole map instance. This and CollectMap are the only places that decide who "nearby" is.
    // Local chat reaches the players in the sender's chunk interest set (docs/adr/ADR-029 D1, ADR-022); without a chunk index, the whole instance.
    private void CollectLocal(PlayerPresence sender, List<Guid> into)
    {
        if (_chunks is null)
        {
            this.CollectInstance(sender, into);
            return;
        }

        List<PlayerPresence> players = [];
        _chunks.CopyVisiblePlayers(sender, players);
        foreach (PlayerPresence player in players)
        {
            into.Add(player.AccountId);
        }
    }

    private void CollectMap(PlayerPresence sender, List<Guid> into) => this.CollectInstance(sender, into);

    private void CollectInstance(PlayerPresence sender, List<Guid> into)
    {
        List<PlayerPresence> players = [];
        _presence.CopyPlayers(sender.Key, players);
        foreach (PlayerPresence player in players)
        {
            into.Add(player.AccountId);
        }
    }

    private async Task<bool> CollectCoopAsync(Guid senderId, List<Guid> into, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            CoopMember? mine = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == senderId, cancellationToken).ConfigureAwait(false);
            if (mine is null)
            {
                return false;
            }

            Guid coopId = mine.CoopId;
            List<CoopMember> members = await unit.Repository<CoopMember>().ListAsync(m => m.CoopId == coopId, cancellationToken).ConfigureAwait(false);
            foreach (CoopMember member in members)
            {
                if (_sessions.GetActive(member.PlayerId) is not null)
                {
                    into.Add(member.PlayerId);
                }
            }

            return true;
        }
    }

    // A whisper goes to one online player. A player who blocked the sender looks offline; a player the sender blocked is refused honestly.
    private async Task<(ChatStatus Status, Guid? TargetId, string Nickname)> ResolveWhisperAsync(Guid senderId, string? target, CancellationToken cancellationToken)
    {
        if (!BlockList.TryKey(target, out string key))
        {
            return (ChatStatus.InvalidNickname, null, string.Empty);
        }

        Guid targetId;
        string nickname;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (player is null)
            {
                return (ChatStatus.UnknownPlayer, null, string.Empty);
            }

            targetId = player.Id;
            nickname = player.Nickname;
        }

        if (targetId == senderId)
        {
            return (ChatStatus.SelfWhisper, null, string.Empty);
        }

        if (await _blocks.HasBlockedAsync(senderId, targetId, cancellationToken).ConfigureAwait(false))
        {
            return (ChatStatus.Blocked, null, string.Empty);
        }

        if (_sessions.GetActive(targetId) is null || await _blocks.HasBlockedAsync(targetId, senderId, cancellationToken).ConfigureAwait(false))
        {
            return (ChatStatus.TargetOffline, null, string.Empty);
        }

        return (ChatStatus.Ok, targetId, nickname);
    }

    private void Record(Guid playerId, ChatLine line)
    {
        Ring ring = _rings.GetOrAdd(playerId, static _ => new Ring());
        ring.Add(line, _time.GetUtcNow());

        if (Interlocked.Increment(ref _recorded) % RingSweepEvery == 0)
        {
            DateTimeOffset cutoff = _time.GetUtcNow() - s_ringIdle;
            foreach (KeyValuePair<Guid, Ring> pair in _rings)
            {
                if (pair.Value.LastUsed < cutoff)
                {
                    _ = _rings.TryRemove(pair);
                }
            }
        }
    }

    private List<ChatLine> Snapshot(Guid playerId) => _rings.TryGetValue(playerId, out Ring? ring) ? ring.ToList() : [];

    private sealed record ChatLine(ChatChannel Channel, Guid SenderId, string SenderNickname, string ToNickname, string Text, DateTimeOffset At);

    private sealed class Ring
    {
        private readonly Lock _gate = new();
        private readonly Queue<ChatLine> _lines = new(ChatRules.ReportContextLines + 1);

        public DateTimeOffset LastUsed { get; private set; }

        public void Add(ChatLine line, DateTimeOffset now)
        {
            lock (_gate)
            {
                _lines.Enqueue(line);
                while (_lines.Count > ChatRules.ReportContextLines)
                {
                    _ = _lines.Dequeue();
                }

                this.LastUsed = now;
            }
        }

        public List<ChatLine> ToList()
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }
}
