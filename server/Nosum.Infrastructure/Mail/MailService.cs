// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.S2C;
using Nosum.Domain.Mail;
using Nosum.Infrastructure.Caching;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;

namespace Nosum.Infrastructure.Mail;

/// <summary>
/// <see cref="IMailService"/> over PostgreSQL (docs/adr/ADR-029 D5). An attachment is handed over by one <see cref="ITransferService"/> call whose
/// enlistment stamps <see cref="MailMessage.CollectedAt"/> in the same transaction: the stamp is a concurrency token and the transfer holds the
/// recipient's inventory lock, so two collects of one mail (a double click, two tabs) can never both pay out. Expired mail is hidden at once
/// and removed by <see cref="MailExpiryService"/>.
/// </summary>
public sealed class MailService : IMailService
{
    // A mailbox lists MailRules.MaxListed mails; storage is bounded a little above so a busy seller's old notices make room for new ones.
    private const int StoredLimit = 200;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ITransferService _transfers;
    private readonly IPlayerNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly WriteBehind? _writeBehind;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="transfers">Hands over attachments atomically.</param>
    /// <param name="notifier">Pushes the unread count to online players.</param>
    /// <param name="time">The clock for sent and expiry times; tests pass a fake one.</param>
    /// <param name="writeBehind">Where the read flag of a mail waits for its write (docs/adr/ADR-035 D7); <see langword="null"/> writes it at once.</param>
    public MailService(IUnitOfWorkFactory unitOfWork, ITransferService transfers, IPlayerNotifier notifier, TimeProvider time, WriteBehind? writeBehind = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _transfers = transfers;
        _notifier = notifier;
        _time = time;
        _writeBehind = writeBehind;
    }

    /// <inheritdoc/>
    public async Task<Guid> SendAsync(Guid recipientId, string code, IReadOnlyList<string> args, MailAttachments? attachments = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(args);

        List<MailItem> lines = MergeLines(attachments);
        long coins = attachments?.Coins ?? 0;
        if (coins < 0 || coins > CoinRules.MaxBalance || lines.Count > MailRules.MaxItemLines)
        {
            throw new ArgumentException("The attachment is out of range.", nameof(attachments));
        }

        foreach (MailItem line in lines)
        {
            if (line.Quantity <= 0 || !ItemCatalog.TryGet(line.ItemId, out _))
            {
                throw new ArgumentException("The attachment names an unknown item or a bad quantity.", nameof(attachments));
            }
        }

        DateTimeOffset now = _time.GetUtcNow();
        MailMessage mail = new()
        {
            RecipientId = recipientId,
            Code = Clip(code, 64),
            Args = [.. args.Take(MailRules.MaxArgs).Select(a => Clip(a, MailRules.MaxArgLength))],
            SentAt = now,
            ExpiresAt = now + MailRules.Lifetime,
            Coins = coins,
        };

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<MailMessage> mails = unit.Repository<MailMessage>();
            mails.Add(mail);
            foreach (MailItem line in lines)
            {
                unit.Repository<MailAttachmentItem>().Add(new MailAttachmentItem { MailId = mail.Id, ItemId = line.ItemId, Quantity = line.Quantity });
            }

            await this.MakeRoomAsync(unit, recipientId, now, cancellationToken).ConfigureAwait(false);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await this.PushUnreadAsync(recipientId, cancellationToken).ConfigureAwait(false);
        return mail.Id;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MailView>> ListAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<MailMessage> mails = await unit.Repository<MailMessage>()
                .PageAsync(m => m.RecipientId == playerId && m.ExpiresAt > now, m => m.SentAt, 0, MailRules.MaxListed, cancellationToken).ConfigureAwait(false);
            List<Guid> ids = [.. mails.Select(m => m.Id)];
            List<MailAttachmentItem> items = ids.Count == 0
                ? []
                : await unit.Repository<MailAttachmentItem>().ListAsync(i => ids.Contains(i.MailId), cancellationToken).ConfigureAwait(false);

            List<MailView> views = new(mails.Count);
            foreach (MailMessage mail in mails)
            {
                List<MailItem> lines = [.. items.Where(i => i.MailId == mail.Id).OrderBy(i => i.ItemId).Select(i => new MailItem(i.ItemId, i.Quantity))];
                views.Add(new MailView(mail.Id, mail.Code, mail.Args, mail.SentAt, mail.ExpiresAt, mail.ReadAt is not null || this.IsReadPending(playerId, mail.Id), mail.Coins, lines, mail.CollectedAt is not null));
            }

            return views;
        }
    }

    /// <inheritdoc/>
    public async Task<int> CountUnreadAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (_writeBehind is { } waiting && waiting.HasPending(playerId))
            {
                // Mails read a moment ago may not be written yet: they do not count.
                List<MailMessage> unread = await unit.Repository<MailMessage>().ListAsync(m => m.RecipientId == playerId && m.ReadAt == null && m.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
                return unread.Count(m => !this.IsReadPending(playerId, m.Id));
            }

            return await unit.Repository<MailMessage>().CountAsync(m => m.RecipientId == playerId && m.ReadAt == null && m.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<MailStatus> ReadAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            MailMessage? mail = await unit.Repository<MailMessage>().FirstOrDefaultAsync(m => m.Id == mailId && m.RecipientId == playerId, cancellationToken).ConfigureAwait(false);
            if (mail is null)
            {
                return MailStatus.NotFound;
            }

            if (mail.ExpiresAt <= now)
            {
                return MailStatus.Expired;
            }

            if (mail.ReadAt is not null || this.IsReadPending(playerId, mailId))
            {
                return MailStatus.Ok;
            }

            if (_writeBehind is not null)
            {
                // Remembered at once, written within the window or when the player leaves (docs/adr/ADR-035 D7): a crash shows the mail as unread again.
                _writeBehind.Stage(playerId, ReadKey(mailId), now, WriteBehind.DefaultWindow, (staged, at) => staged.Repository<MailMessage>().Update(new MailMessage { Id = mailId, ReadAt = at }, nameof(MailMessage.ReadAt)));
            }
            else
            {
                mail.ReadAt = now;
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (unit.IsConcurrencyConflict(ex))
                {
                    return MailStatus.Conflict;
                }
            }
        }

        await this.PushUnreadAsync(playerId, cancellationToken).ConfigureAwait(false);
        return MailStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<MailStatus> CollectAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        long coins;
        List<MailItem> lines;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            MailMessage? mail = await unit.Repository<MailMessage>().FirstOrDefaultAsync(m => m.Id == mailId && m.RecipientId == playerId, cancellationToken).ConfigureAwait(false);
            if (mail is null)
            {
                return MailStatus.NotFound;
            }

            if (mail.ExpiresAt <= now)
            {
                return MailStatus.Expired;
            }

            lines = [.. (await unit.Repository<MailAttachmentItem>().ListAsync(i => i.MailId == mailId, cancellationToken).ConfigureAwait(false)).Select(i => new MailItem(i.ItemId, i.Quantity))];
            coins = mail.Coins;
            if (lines.Count == 0 && coins == 0)
            {
                return MailStatus.NothingToCollect;
            }

            if (mail.CollectedAt is not null)
            {
                return MailStatus.AlreadyCollected;
            }
        }

        List<TransferLeg> legs = [.. lines.Select(l => new TransferLeg(TransferParty.Player(playerId), l.ItemId, l.Quantity))];
        legs.Add(new TransferLeg(TransferParty.Player(playerId), 0, 0, coins));

        // Stamped inside the transfer's own transaction: the hand-over and the "collected" mark commit together or not at all.
        TransferStatus status = await _transfers.ExecuteAsync(
            legs,
            "mail.collect",
            async (inner, token) =>
            {
                MailMessage? tracked = await inner.Repository<MailMessage>().FindAsync(mailId, token).ConfigureAwait(false);
                if (tracked is null || tracked.CollectedAt is not null)
                {
                    return TransferStatus.Refused;
                }

                tracked.CollectedAt = now;
                tracked.ReadAt ??= now;
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);

        MailStatus result = status switch
        {
            TransferStatus.Ok => MailStatus.Ok,
            TransferStatus.NoSpace => MailStatus.NoSpace,
            TransferStatus.TooBig => MailStatus.TooRich,
            TransferStatus.Refused => MailStatus.AlreadyCollected,
            TransferStatus.Conflict => MailStatus.Conflict,
            _ => MailStatus.Failed,
        };

        if (result == MailStatus.Ok)
        {
            // Collecting stamps the mail as read in its own transaction.
            _writeBehind?.Discard(playerId, ReadKey(mailId));
            await this.PushUnreadAsync(playerId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<MailStatus> DeleteAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<MailMessage> mails = unit.Repository<MailMessage>();
            MailMessage? mail = await mails.FirstOrDefaultAsync(m => m.Id == mailId && m.RecipientId == playerId, cancellationToken).ConfigureAwait(false);
            if (mail is null)
            {
                return MailStatus.NotFound;
            }

            if (mail.CollectedAt is null && (mail.Coins > 0 || await unit.Repository<MailAttachmentItem>().AnyAsync(i => i.MailId == mailId, cancellationToken).ConfigureAwait(false)))
            {
                return MailStatus.HasAttachments;
            }

            mails.Remove(mail);
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsConcurrencyConflict(ex))
            {
                return MailStatus.Conflict;
            }
        }

        _writeBehind?.Discard(playerId, ReadKey(mailId));
        await this.PushUnreadAsync(playerId, cancellationToken).ConfigureAwait(false);
        return MailStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        const int batch = 500;
        int removed = 0;
        while (true)
        {
            DateTimeOffset now = _time.GetUtcNow();
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<MailMessage> mails = unit.Repository<MailMessage>();
                List<MailMessage> due = await mails.PageAsync(m => m.ExpiresAt <= now, m => m.ExpiresAt, 0, batch, cancellationToken).ConfigureAwait(false);
                foreach (MailMessage mail in due)
                {
                    mails.Remove(mail);
                }

                if (due.Count == 0)
                {
                    return removed;
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                removed += due.Count;
                if (due.Count < batch)
                {
                    return removed;
                }
            }
        }
    }

    // Keeps one mailbox bounded: past StoredLimit the oldest mails that carry nothing left to collect (or have expired) are dropped to make room.
    private async Task MakeRoomAsync(IUnitOfWork unit, Guid recipientId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IRepository<MailMessage> mails = unit.Repository<MailMessage>();
        int stored = await mails.CountAsync(m => m.RecipientId == recipientId, cancellationToken).ConfigureAwait(false);
        if (stored < StoredLimit)
        {
            return;
        }

        int excess = stored - StoredLimit + 1;
        List<MailMessage> candidates = await mails.ListAsync(
            m => m.RecipientId == recipientId && (m.ExpiresAt <= now || m.Coins == 0 || m.CollectedAt != null),
            cancellationToken).ConfigureAwait(false);

        foreach (MailMessage old in candidates.OrderBy(m => m.SentAt).Take(excess))
        {
            // A mail with item lines is not droppable unless collected or expired: check them.
            if (old.ExpiresAt > now && old.CollectedAt is null && await unit.Repository<MailAttachmentItem>().AnyAsync(i => i.MailId == old.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            mails.Remove(old);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A failed push must not undo the committed mail; the player reads the mailbox when they next open it.")]
    private static string ReadKey(Guid mailId) => "mail.read:" + mailId.ToString("N");

    private bool IsReadPending(Guid playerId, Guid mailId) => _writeBehind is not null && _writeBehind.TryPeek(playerId, ReadKey(mailId), out DateTimeOffset _);

    private async Task PushUnreadAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            int unread = await this.CountUnreadAsync(playerId, cancellationToken).ConfigureAwait(false);
            using PacketScope<MailUnread> lease = PacketFactory<MailUnread>.Acquire();
            lease.Value.Count = unread;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // See above.
        }
    }

    private static List<MailItem> MergeLines(MailAttachments? attachments)
    {
        Dictionary<int, int> merged = [];
        if (attachments is not null)
        {
            foreach (MailItem line in attachments.Items)
            {
                merged[line.ItemId] = merged.GetValueOrDefault(line.ItemId) + line.Quantity;
            }
        }

        return [.. merged.OrderBy(p => p.Key).Select(p => new MailItem(p.Key, p.Value))];
    }

    private static string Clip(string? text, int max)
    {
        string value = text ?? string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
