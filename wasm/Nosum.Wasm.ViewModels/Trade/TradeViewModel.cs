// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Trade;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Trade;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Trade;

/// <summary>A player the local player may invite: their id on the map and the text to show.</summary>
/// <param name="Id">The id on the map.</param>
/// <param name="Label">The localized label.</param>
public sealed record PlayerChoice(uint Id, string Label);

/// <summary>What the trade window shows.</summary>
public enum TradePhase
{
    /// <summary>No trade: pick a player.</summary>
    Idle = 0,

    /// <summary>An invitation was sent and waits.</summary>
    Waiting = 1,

    /// <summary>An invitation arrived.</summary>
    Invited = 2,

    /// <summary>A trade is open.</summary>
    Open = 3,
}

/// <summary>
/// View model of the trade window: invite a player on the map, put items and coins in the offer, confirm. Offers live on the server;
/// every click sends the whole new offer and the window shows what the server pushes back, so both sides always see the same trade and
/// a change clears the confirmations on both screens at once.
/// </summary>
public sealed class TradeViewModel : PanelViewModel, IDisposable
{
    private readonly TradeChannel _trade;
    private readonly NearbyPlayers _nearby;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private TradeEndReason? _ended;

    /// <summary>Initializes the view model.</summary>
    /// <param name="trade">The trade channel.</param>
    /// <param name="nearby">The other players on the map.</param>
    /// <param name="inventory">The inventory view model (what can be offered).</param>
    /// <param name="describer">Describes offered items.</param>
    /// <param name="text">UI text.</param>
    public TradeViewModel(TradeChannel trade, NearbyPlayers nearby, InventoryViewModel inventory, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);

        _trade = trade;
        _nearby = nearby;
        _inventory = inventory;
        _describer = describer;
        trade.Changed += this.OnChanged;
        trade.Ended += this.OnEnded;
        trade.Completed += this.OnCompleted;
        nearby.Changed += this.Raise;
        inventory.Changed += this.Raise;
    }

    /// <summary>Raised when the window should open (an invitation arrived or a trade began) or close (it ended). May run on any thread.</summary>
    public event Action<bool>? WantsOpen;

    /// <summary>Gets what the window shows.</summary>
    public TradePhase Phase => _trade.Open is not null ? TradePhase.Open : _trade.Invite != 0 ? TradePhase.Invited : _trade.Waiting ? TradePhase.Waiting : TradePhase.Idle;

    /// <summary>Gets the localized label of the player who invited, for the invitation dialog.</summary>
    public string InviterLabel => this.Text("Player_Label", _trade.Invite);

    /// <summary>Gets the players on the map that can be invited.</summary>
    public IReadOnlyList<PlayerChoice> Players
    {
        get
        {
            List<PlayerChoice> players = [];
            foreach (uint id in _nearby.Ids())
            {
                players.Add(new PlayerChoice(id, this.Text("Player_Label", id)));
            }

            return players;
        }
    }

    /// <summary>Gets the label of the other party of the open trade.</summary>
    public string OtherLabel => _trade.Open is { } open ? this.Text("Player_Label", open.OtherPlayerId) : string.Empty;

    /// <summary>Gets or sets the coins typed in the offer.</summary>
    public string CoinsText { get; set; } = "0";

    /// <summary>Gets the player's offered items.</summary>
    public IReadOnlyList<SlotDisplay> MyItems => this.Describe(_trade.Open?.MyItems);

    /// <summary>Gets the other party's offered items.</summary>
    public IReadOnlyList<SlotDisplay> OtherItems => this.Describe(_trade.Open?.OtherItems);

    /// <summary>Gets the player's offered coins, formatted.</summary>
    public string MyCoins => Number(_trade.Open?.MyCoins ?? 0);

    /// <summary>Gets the other party's offered coins, formatted.</summary>
    public string OtherCoins => Number(_trade.Open?.OtherCoins ?? 0);

    /// <summary>Gets whether the player confirmed.</summary>
    public bool MyConfirmed => _trade.Open?.MyConfirmed ?? false;

    /// <summary>Gets whether the other party confirmed.</summary>
    public bool OtherConfirmed => _trade.Open?.OtherConfirmed ?? false;

    /// <summary>Gets the inventory, for choosing what to offer.</summary>
    public IReadOnlyList<SlotDisplay> InventorySlots => _inventory.Slots;

    /// <summary>Invites a player.</summary>
    /// <param name="id">The id on the map.</param>
    public void Invite(uint id)
    {
        this.ClearStatus();
        _trade.Request(id);
    }

    /// <summary>Answers the invitation.</summary>
    /// <param name="accept">Whether to accept.</param>
    public void Respond(bool accept) => _trade.Respond(accept);

    /// <summary>Cancels the trade or the invitation.</summary>
    public void Cancel() => _trade.Cancel();

    /// <summary>Confirms the offers.</summary>
    public void Confirm() => _trade.Confirm();

    /// <summary>Adds a whole inventory stack to the offer (or more of an item already in it).</summary>
    /// <param name="inventorySlot">The inventory slot.</param>
    public void Offer(int inventorySlot)
    {
        if (_trade.Open is not { } open)
        {
            return;
        }

        foreach (SlotDisplay slot in _inventory.Slots)
        {
            if (slot.Index != inventorySlot || slot.IsEmpty)
            {
                continue;
            }

            List<TradeLineInfo> lines = [.. open.MyItems];
            int at = lines.FindIndex(l => l.ItemId == slot.ItemId);
            if (at >= 0)
            {
                lines[at] = new TradeLineInfo(slot.ItemId, lines[at].Quantity + slot.Quantity);
            }
            else
            {
                lines.Add(new TradeLineInfo(slot.ItemId, slot.Quantity));
            }

            _trade.SetOffer(lines, this.ParseCoins(open.MyCoins));
            return;
        }
    }

    /// <summary>Takes a line out of the offer.</summary>
    /// <param name="index">Index of the line in <see cref="MyItems"/>.</param>
    public void Withdraw(int index)
    {
        if (_trade.Open is not { } open || (uint)index >= (uint)open.MyItems.Count)
        {
            return;
        }

        List<TradeLineInfo> lines = [.. open.MyItems];
        lines.RemoveAt(index);
        _trade.SetOffer(lines, open.MyCoins);
    }

    /// <summary>Sends the typed coins as the offered coins.</summary>
    public void SetCoins()
    {
        if (_trade.Open is not { } open)
        {
            return;
        }

        _trade.SetOffer(open.MyItems, this.ParseCoins(open.MyCoins));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _trade.Changed -= this.OnChanged;
        _trade.Ended -= this.OnEnded;
        _trade.Completed -= this.OnCompleted;
        _nearby.Changed -= this.Raise;
        _inventory.Changed -= this.Raise;
    }

    private long ParseCoins(long fallback)
        => long.TryParse(this.CoinsText, NumberStyles.None, CultureInfo.InvariantCulture, out long coins) ? coins : fallback;

    private List<SlotDisplay> Describe(IReadOnlyList<TradeLineInfo>? lines)
    {
        List<SlotDisplay> items = [];
        if (lines is null)
        {
            return items;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            items.Add(_describer.Describe(i, lines[i].ItemId, lines[i].Quantity));
        }

        return items;
    }

    private void OnChanged()
    {
        if (_trade.Open is { } open)
        {
            this.CoinsText = open.MyCoins.ToString(CultureInfo.InvariantCulture);
            _ended = null;
            this.WantsOpen?.Invoke(true);
        }
        else if (_trade.Invite != 0)
        {
            this.WantsOpen?.Invoke(true);
        }

        this.Raise();
    }

    private void OnEnded(TradeEndReason reason)
    {
        _ended = reason;
        this.SetStatus(this.Text("Trade_Ended_" + reason));
    }

    private void OnCompleted(TradeStatus status)
    {
        if (status != TradeStatus.Ok)
        {
            this.SetStatus(this.Text("Trade_Status_" + status));
        }
    }
}
