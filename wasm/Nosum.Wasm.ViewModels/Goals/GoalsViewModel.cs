// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Goals;
using Nosum.Gameplay.Community;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Goals;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Goals;

/// <summary>One goal of the board, already localized.</summary>
/// <param name="GoalId">The goal id.</param>
/// <param name="Name">The goal's name ("Spring bridge").</param>
/// <param name="RewardText">What completing it gives, one line.</param>
/// <param name="Item">The item that is delivered, one unit, for the slot control.</param>
/// <param name="Percent">How full the bar is, 0 to 100.</param>
/// <param name="ProgressText">"1,200 / 5,000".</param>
/// <param name="MineText">"You delivered 40".</param>
/// <param name="ContributorsText">"17 players took part".</param>
/// <param name="State">Where the goal is in its life.</param>
/// <param name="StateText">The state in words (Open, Completed, Expired).</param>
/// <param name="Held">How many of the item the player carries.</param>
/// <param name="AmountText">The amount typed for the delivery.</param>
/// <param name="CanDeliver">Whether the goal is open and the player has the item.</param>
public sealed record GoalDisplay(int GoalId, string Name, string RewardText, SlotDisplay Item, int Percent, string ProgressText, string MineText, string ContributorsText, GoalState State, string StateText, int Held, string AmountText, bool CanDeliver);

/// <summary>
/// View model of the Town board page (docs/adr/ADR-030): one row per goal of the season with its progress bar, this player's part and an amount to
/// deliver. The catalogue (item, target, reward) is the shared <see cref="CommunityGoalCatalog"/>; the totals come from the server and
/// every delivery is an intent the server counts. One instance serves the page for the whole session.
/// </summary>
public sealed class GoalsViewModel : PanelViewModel, IDisposable
{
    private readonly GoalChannel _channel;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private readonly CalendarChannel _calendar;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly ConcurrentDictionary<int, string> _amounts = [];

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Reads and delivers.</param>
    /// <param name="inventory">What the player carries.</param>
    /// <param name="describer">Describes the delivered items.</param>
    /// <param name="calendar">The server calendar, for the time the season has left.</param>
    /// <param name="catalogue">Goal names and rewards.</param>
    /// <param name="text">UI text.</param>
    public GoalsViewModel(GoalChannel channel, InventoryViewModel inventory, ItemDescriber describer, CalendarChannel calendar, IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(catalogue);

        _channel = channel;
        _inventory = inventory;
        _describer = describer;
        _calendar = calendar;
        _catalogue = catalogue;
        channel.Changed += this.Raise;
        channel.Delivered += this.OnDelivered;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets the title of the board page.</summary>
    public string Title => this.Text("Goals_Title");

    /// <summary>Gets the line that says how long the season lasts, or an empty string until the server has said.</summary>
    public string SeasonEndText
    {
        get
        {
            GoalsInfo info = _channel.Goals;
            if (info.SeasonEndsAtMs <= 0 || !_calendar.IsLoaded || _calendar.DayLengthMs <= 0)
            {
                return string.Empty;
            }

            long left = info.SeasonEndsAtMs - _calendar.ServerNowMs();
            long days = Math.Max(0, (left + _calendar.DayLengthMs - 1) / _calendar.DayLengthMs);
            return this.Text("Goals_SeasonEnds", days);
        }
    }

    /// <summary>Gets whether the server has sent the goals yet.</summary>
    public bool IsLoaded => _channel.Goals.SeasonEndsAtMs > 0;

    /// <summary>Gets the goals of the board, in catalogue order.</summary>
    public IReadOnlyList<GoalDisplay> Goals
    {
        get
        {
            List<GoalDisplay> rows = [];
            foreach (GoalInfo info in _channel.Goals.Goals)
            {
                if (CommunityGoalCatalog.TryGet(info.GoalId, out CommunityGoalDefinition? goal))
                {
                    rows.Add(this.Describe(goal, info));
                }
            }

            return rows;
        }
    }

    /// <summary>Opens the page: reads the goals again and forgets the amounts typed last time.</summary>
    public void Open()
    {
        _amounts.Clear();
        this.ClearStatus();
        _channel.Load();
        this.Raise();
    }

    /// <summary>Sets the amount typed for a goal.</summary>
    /// <param name="goalId">The goal.</param>
    /// <param name="text">The typed text.</param>
    public void SetAmount(int goalId, string text)
    {
        _amounts[goalId] = text ?? string.Empty;
        this.Raise();
    }

    /// <summary>Delivers the typed amount into a goal.</summary>
    /// <param name="goalId">The goal.</param>
    public void Deliver(int goalId)
    {
        this.ClearStatus();
        string typed = _amounts.TryGetValue(goalId, out string? text) ? text : this.DefaultAmount(goalId);
        if (!int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) || quantity < 1)
        {
            this.SetStatus(this.Text("Goals_Status_" + GoalStatus.InvalidQuantity));
            return;
        }

        _channel.Deliver(goalId, quantity);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Changed -= this.Raise;
        _channel.Delivered -= this.OnDelivered;
        _inventory.Changed -= this.Raise;
    }

    private GoalDisplay Describe(CommunityGoalDefinition goal, GoalInfo info)
    {
        long target = goal.Target;
        int percent = (int)Math.Clamp(info.Total * 100 / Math.Max(1, target), 0, 100);
        int held = this.HeldOf(goal.ItemId);
        bool open = info.State == GoalState.Active;
        string amount = _amounts.TryGetValue(goal.Id, out string? typed) ? typed : this.DefaultAmount(goal.Id);
        return new GoalDisplay(
            goal.Id,
            _catalogue["Goal_" + goal.Key + "_Name"].Value,
            _catalogue["Goal_" + goal.Key + "_Reward"].Value,
            _describer.Describe(0, goal.ItemId, 1),
            percent,
            this.Text("Goals_Progress", Number(Math.Min(info.Total, target)), Number(target)),
            this.Text("Goals_Mine", Number(info.Mine)),
            this.Text("Goals_Contributors", info.Contributors),
            info.State,
            this.Text("Goals_State_" + info.State),
            held,
            amount,
            open && held > 0);
    }

    // What the amount box shows until the player types: everything they carry that the goal still takes, at least one.
    private string DefaultAmount(int goalId)
    {
        if (!CommunityGoalCatalog.TryGet(goalId, out CommunityGoalDefinition? goal))
        {
            return "1";
        }

        long total = 0;
        foreach (GoalInfo info in _channel.Goals.Goals)
        {
            if (info.GoalId == goalId)
            {
                total = info.Total;
            }
        }

        long fit = Math.Min(this.HeldOf(goal.ItemId), CommunityGoalRules.Remaining(goal, total));
        return Math.Max(1, Math.Min(fit, CommunityGoalRules.MaxDeliveryPerRequest)).ToString(CultureInfo.InvariantCulture);
    }

    private int HeldOf(int itemId)
    {
        int count = 0;
        foreach (SlotDisplay slot in _inventory.Slots)
        {
            if (slot.ItemId == itemId)
            {
                count += slot.Quantity;
            }
        }

        return count;
    }

    private void OnDelivered(GoalStatus status, int delivered)
    {
        if (status == GoalStatus.Ok)
        {
            _amounts.Clear();
            this.SetStatus(this.Text("Goals_Done", delivered), good: true);
        }
        else
        {
            this.SetStatus(this.Text("Goals_Status_" + status));
        }
    }
}
