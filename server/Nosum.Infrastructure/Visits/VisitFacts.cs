// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Visits;
using Nosum.Infrastructure.Caching;

namespace Nosum.Infrastructure.Visits;

/// <summary>What an owner decided about who may enter their places.</summary>
/// <param name="Listed">The players on the allow-list, oldest first.</param>
/// <param name="CoopMembersMayVisit">Whether members of the owner's co-op may enter.</param>
/// <param name="FriendsMayVisit">Whether friends may enter.</param>
public sealed record OwnerVisitFacts(IReadOnlyList<Guid> Listed, bool CoopMembersMayVisit, bool FriendsMayVisit);

/// <summary>
/// The allow-list and the two settings of an owner, read through a cache (docs/adr/ADR-035 D5). Two services write these rows (<c>VisitService</c> the list and the co-op
/// setting, <c>FriendService</c> the friends setting) and both call <see cref="Changed"/> after the save, so the access check that runs on every entry reads memory and
/// still sees a revoke at once.
/// </summary>
public sealed class VisitFacts
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ReadThroughCache<Guid, IReadOnlyList<Guid>> _listed;
    private readonly ReadThroughCache<Guid, VisitSettingValues> _settings;

    /// <summary>Initializes the facts.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="cached">Whether the facts are kept; <see langword="false"/> reads the rows every time.</param>
    public VisitFacts(IUnitOfWorkFactory unitOfWork, bool cached = true)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _listed = new ReadThroughCache<Guid, IReadOnlyList<Guid>>(cached);
        _settings = new ReadThroughCache<Guid, VisitSettingValues>(cached);
    }

    /// <summary>Gets what an owner decided: one read for the list and one for the settings the first time, memory afterwards.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The allow-list and the settings (the defaults for an owner who never changed one).</returns>
    public async ValueTask<OwnerVisitFacts> GetAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        VisitSettingValues settings = await _settings.GetAsync(ownerId, ct => this.LoadSettingsAsync(ownerId, ct), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Guid> listed = await this.GetListedAsync(ownerId, cancellationToken).ConfigureAwait(false);
        return new OwnerVisitFacts(listed, settings.CoopMembersMayVisit, settings.FriendsMayVisit);
    }

    /// <summary>Gets whether friends may visit (one read the first time, memory afterwards).</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The setting; the default for an owner who never changed it.</returns>
    public async ValueTask<bool> FriendsMayVisitAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => (await _settings.GetAsync(ownerId, ct => this.LoadSettingsAsync(ownerId, ct), cancellationToken).ConfigureAwait(false)).FriendsMayVisit;

    /// <summary>Gets only the allow-list, oldest first (for the calls that edit it).</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The listed players.</returns>
    public ValueTask<IReadOnlyList<Guid>> GetListedAsync(Guid ownerId, CancellationToken cancellationToken = default)
        => _listed.GetAsync(ownerId, ct => this.LoadListedAsync(ownerId, ct), cancellationToken);

    /// <summary>Describes what is held for an owner without loading it, for the audit of the caches (a missing part is left out).</summary>
    /// <param name="ownerId">The owner.</param>
    /// <returns>The held parts as text, or <see langword="null"/> when nothing is held.</returns>
    public string? DescribeHeld(Guid ownerId)
    {
        bool listed = _listed.TryGet(ownerId, out IReadOnlyList<Guid>? list);
        bool settings = _settings.TryGet(ownerId, out VisitSettingValues? values);
        if (!listed && !settings)
        {
            return null;
        }

        return (listed ? "listed=" + string.Join(',', list!.Order()) : string.Empty) + ";" + (settings ? $"coop={values!.CoopMembersMayVisit};friends={values.FriendsMayVisit}" : string.Empty);
    }

    /// <summary>Reads both parts and describes them in the same shape as <see cref="DescribeHeld"/> (use facts that keep nothing).</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts as text.</returns>
    public async ValueTask<string> DescribeAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        OwnerVisitFacts facts = await this.GetAsync(ownerId, cancellationToken).ConfigureAwait(false);
        return $"listed={string.Join(',', facts.Listed.Order())};coop={facts.CoopMembersMayVisit};friends={facts.FriendsMayVisit}";
    }

    /// <summary>Drops the allow-list of an owner; call it after a save that added or removed a grant.</summary>
    /// <param name="ownerId">The owner.</param>
    public void ListChanged(Guid ownerId) => _listed.Invalidate(ownerId);

    /// <summary>Drops the settings of an owner; call it after a save that changed one.</summary>
    /// <param name="ownerId">The owner.</param>
    public void SettingsChanged(Guid ownerId) => _settings.Invalidate(ownerId);

    /// <summary>Drops everything known about an owner.</summary>
    /// <param name="ownerId">The owner.</param>
    public void Changed(Guid ownerId)
    {
        this.ListChanged(ownerId);
        this.SettingsChanged(ownerId);
    }

    private async Task<IReadOnlyList<Guid>> LoadListedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<VisitGrant> grants = await unit.Repository<VisitGrant>().ListAsync(g => g.OwnerId == ownerId, cancellationToken).ConfigureAwait(false);
            return [.. grants.OrderBy(g => g.CreatedAt).Select(g => g.VisitorId)];
        }
    }

    private async Task<VisitSettingValues> LoadSettingsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            return new VisitSettingValues(
                settings?.CoopMembersMayVisit ?? VisitRules.CoopMembersMayVisitByDefault,
                settings?.FriendsMayVisit ?? VisitRules.FriendsMayVisitByDefault);
        }
    }

    private sealed record VisitSettingValues(bool CoopMembersMayVisit, bool FriendsMayVisit);
}
