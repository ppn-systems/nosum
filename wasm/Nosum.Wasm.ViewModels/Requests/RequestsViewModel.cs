// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Requests;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Requests;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Requests;

/// <summary>One request of today, already localized.</summary>
/// <param name="Slot">The request's slot in its day.</param>
/// <param name="Item">The item to deliver with the requested quantity, for the slot control.</param>
/// <param name="AskText">"Deliver 6 × Parsnip".</param>
/// <param name="RewardText">"Reward: 39 coins".</param>
/// <param name="Held">How many of the item the player carries.</param>
/// <param name="HeldText">"You carry 4".</param>
/// <param name="Filled">Whether the player already filled it today.</param>
/// <param name="FilledText">"Done for today".</param>
/// <param name="CanDeliver">Whether it is not filled and the player carries enough.</param>
public sealed record RequestDisplay(int Slot, SlotDisplay Item, string AskText, string RewardText, int Held, string HeldText, bool Filled, string FilledText, bool CanDeliver);

/// <summary>
/// View model of the requests tab of the Town board (docs/adr/ADR-036): today's requests with what each asks and pays, and a button to fill it. The requests,
/// the reward and every outcome are the server's; this only asks and shows. One instance serves the tab for the whole session.
/// </summary>
public sealed class RequestsViewModel : PanelViewModel, IDisposable
{
    private readonly RequestChannel _channel;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private readonly CalendarChannel _calendar;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Reads and delivers.</param>
    /// <param name="inventory">What the player carries.</param>
    /// <param name="describer">Describes the requested items.</param>
    /// <param name="calendar">The server calendar, for the time until the next requests.</param>
    /// <param name="text">UI text.</param>
    public RequestsViewModel(RequestChannel channel, InventoryViewModel inventory, ItemDescriber describer, CalendarChannel calendar, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(calendar);

        _channel = channel;
        _inventory = inventory;
        _describer = describer;
        _calendar = calendar;
        channel.Changed += this.Raise;
        channel.Delivered += this.OnDelivered;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets whether the server has sent today's requests yet.</summary>
    public bool IsLoaded => _channel.Requests.Day > 0;

    /// <summary>Gets the line that says how long until the next requests come, or an empty string until the server has said.</summary>
    public string NextText
    {
        get
        {
            RequestsInfo info = _channel.Requests;
            if (info.Day <= 0 || !_calendar.IsLoaded)
            {
                return string.Empty;
            }

            // Rounded up to the minute: "0 h 0 min" would be shown for a whole minute otherwise.
            int minutes = (int)Math.Ceiling(Math.Max(0, info.DayEndsAtMs - _calendar.ServerNowMs()) / 60_000.0);
            return this.Text("Requests_NewIn", minutes / 60, minutes % 60);
        }
    }

    /// <summary>Gets the line that says how long the early bonus of today lasts, or an empty string once it is over or before the server has said.</summary>
    public string BonusText
    {
        get
        {
            RequestsInfo info = _channel.Requests;
            if (info.Day <= 0 || !_calendar.IsLoaded)
            {
                return string.Empty;
            }

            long leftMs = info.EarlyUntilMs - _calendar.ServerNowMs();
            if (leftMs <= 0)
            {
                return string.Empty;
            }

            // Rounded up to the minute, as NextText is.
            int minutes = (int)Math.Ceiling(leftMs / 60_000.0);
            return this.Text("Requests_Bonus", info.EarlyBonusPercent, minutes / 60, minutes % 60);
        }
    }

    /// <summary>Gets today's requests, by slot.</summary>
    public IReadOnlyList<RequestDisplay> Requests
    {
        get
        {
            List<RequestDisplay> rows = [];
            foreach (RequestInfo info in _channel.Requests.Requests)
            {
                int held = _inventory.CountOf(info.ItemId);
                SlotDisplay item = _describer.Describe(0, info.ItemId, info.Quantity);
                rows.Add(new RequestDisplay(
                    info.Slot,
                    item,
                    this.Text("Requests_Ask", info.Quantity, item.Name),
                    this.Text("Requests_Reward", Number(info.RewardCoins)),
                    held,
                    this.Text("Requests_Held", held),
                    info.Filled,
                    this.Text("Requests_Filled"),
                    !info.Filled && held >= info.Quantity));
            }

            return rows;
        }
    }

    /// <summary>Opens the tab: reads today's requests again.</summary>
    public void Open()
    {
        this.ClearStatus();
        _channel.Load();
        this.Raise();
    }

    /// <summary>Fills a request.</summary>
    /// <param name="slot">The request's slot.</param>
    public void Deliver(int slot)
    {
        this.ClearStatus();
        RequestsInfo info = _channel.Requests;
        if (info.Day > 0)
        {
            _channel.Deliver(info.Day, slot);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Changed -= this.Raise;
        _channel.Delivered -= this.OnDelivered;
        _inventory.Changed -= this.Raise;
    }

    private void OnDelivered(RequestStatus status, long reward, bool bonus)
    {
        if (status == RequestStatus.Ok)
        {
            this.SetStatus(this.Text(bonus ? "Requests_DoneBonus" : "Requests_Done", Number(reward)), good: true);
        }
        else
        {
            this.SetStatus(this.Text("Requests_Status_" + status));
        }
    }
}
