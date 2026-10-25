// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Coop;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Text;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Trade;

namespace Nosum.Wasm.ViewModels.Coop;

/// <summary>One member row of the co-op panel.</summary>
/// <param name="MemberId">The membership id.</param>
/// <param name="Label">The localized label ("You", "Member 2").</param>
/// <param name="RoleText">The localized role.</param>
/// <param name="IsMe">Whether this is the player.</param>
/// <param name="Online">Whether the member is connected.</param>
/// <param name="CanKick">Whether the player may remove this member.</param>
public sealed record MemberDisplay(Guid MemberId, string Label, string RoleText, bool IsMe, bool Online, bool CanKick);

/// <summary>One warehouse row of the co-op panel.</summary>
/// <param name="Item">The stack for the slot control (index is the item id).</param>
/// <param name="Available">How many the player may take out now.</param>
public sealed record StockDisplay(SlotDisplay Item, int Available);

/// <summary>View model of the co-op panel: found a co-op, answer invitations, invite players on the map, manage members, and use the shared warehouse. The server decides every outcome, including who may withdraw what.</summary>
public sealed class CoopViewModel : PanelViewModel, IDisposable
{
    private readonly CoopChannel _coop;
    private readonly NearbyPlayers _nearby;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private int _depositSlot = -1;
    private int _withdrawItem;

    /// <summary>Initializes the view model.</summary>
    /// <param name="coop">The co-op channel.</param>
    /// <param name="nearby">The other players on the map (invitations).</param>
    /// <param name="inventory">The inventory view model (what can be deposited).</param>
    /// <param name="describer">Describes warehouse items.</param>
    /// <param name="text">UI text.</param>
    public CoopViewModel(CoopChannel coop, NearbyPlayers nearby, InventoryViewModel inventory, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(coop);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);

        _coop = coop;
        _nearby = nearby;
        _inventory = inventory;
        _describer = describer;
        coop.Changed += this.Raise;
        coop.Completed += this.OnCompleted;
        nearby.Changed += this.Raise;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets or sets the name typed in the create form.</summary>
    public string NameText { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity typed for a deposit or withdrawal.</summary>
    public string QuantityText { get; set; } = "1";

    /// <summary>Gets whether the player belongs to a co-op.</summary>
    public bool HasCoop => _coop.Current.HasCoop;

    /// <summary>Gets whether the player is the founder.</summary>
    public bool IsFounder => _coop.Current.Role == CoopRole.Founder;

    /// <summary>Gets the co-op's name resolved for the current language (never the raw envelope).</summary>
    public string Name => LocalizedText.Resolve(_coop.Current.Name, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Gets the invitations to the player, with the co-op names resolved.</summary>
    public IReadOnlyList<(Guid Id, string Name)> Invites
    {
        get
        {
            List<(Guid, string)> invites = [];
            string tag = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            foreach (CoopInviteInfo invite in _coop.Current.Invites)
            {
                invites.Add((invite.InviteId, LocalizedText.Resolve(invite.CoopName, tag)));
            }

            return invites;
        }
    }

    /// <summary>Gets the members.</summary>
    public IReadOnlyList<MemberDisplay> Members
    {
        get
        {
            List<MemberDisplay> members = [];
            int number = 0;
            foreach (CoopMemberInfo member in _coop.Current.Members)
            {
                number++;
                string label = member.IsMe ? this.Text("Coop_You") : this.Text("Coop_MemberNumber", number);
                members.Add(new MemberDisplay(member.MemberId, label, this.Text("Coop_Role_" + member.Role), member.IsMe, member.Online, this.IsFounder && !member.IsMe));
            }

            return members;
        }
    }

    /// <summary>Gets the players on the map that can be invited (the founder only).</summary>
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

    /// <summary>Gets the warehouse.</summary>
    public IReadOnlyList<StockDisplay> Stock
    {
        get
        {
            List<StockDisplay> stock = [];
            foreach (CoopStockInfo row in _coop.Current.Stock)
            {
                SlotDisplay item = _describer.Describe(row.ItemId, row.ItemId, row.Quantity, row.ItemId == _withdrawItem);
                stock.Add(new StockDisplay(item, row.Available));
            }

            return stock;
        }
    }

    /// <summary>Gets the warehouse log lines as localized text, newest first.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            List<string> lines = [];
            Dictionary<Guid, int> numbers = [];
            int n = 0;
            foreach (CoopMemberInfo member in _coop.Current.Members)
            {
                numbers[member.MemberId] = ++n;
            }

            foreach (CoopLogInfo entry in _coop.Current.Log)
            {
                string who = entry.MemberId == Guid.Empty || !numbers.TryGetValue(entry.MemberId, out int number) ? this.Text("Coop_FormerMember") : this.Text("Coop_MemberNumber", number);
                string when = DateTimeOffset.FromUnixTimeMilliseconds(entry.AtMs).ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
                lines.Add(this.Text(entry.Delta > 0 ? "Coop_Log_Deposit" : "Coop_Log_Withdraw", when, who, Math.Abs(entry.Delta), _describer.NameOf(entry.ItemId)));
            }

            return lines;
        }
    }

    /// <summary>Gets the inventory with the stack picked for deposit marked.</summary>
    public IReadOnlyList<SlotDisplay> InventorySlots
    {
        get
        {
            List<SlotDisplay> slots = [];
            foreach (SlotDisplay slot in _inventory.Slots)
            {
                slots.Add(slot with { IsSelected = slot.Index == _depositSlot });
            }

            return slots;
        }
    }

    /// <summary>Called when the panel opens: reads the co-op.</summary>
    public void Open()
    {
        this.ClearStatus();
        _coop.Load();
    }

    /// <summary>Founds a co-op with the typed name.</summary>
    public void Create() => _coop.Create(this.NameText);

    /// <summary>Answers an invitation.</summary>
    /// <param name="inviteId">The invitation.</param>
    /// <param name="accept">Whether to join.</param>
    public void Respond(Guid inviteId, bool accept) => _coop.Respond(inviteId, accept);

    /// <summary>Invites a player on the map.</summary>
    /// <param name="id">The id on the map.</param>
    public void Invite(uint id) => _coop.Invite(id);

    /// <summary>Leaves the co-op.</summary>
    public void Leave() => _coop.Leave();

    /// <summary>Removes a member.</summary>
    /// <param name="memberId">The membership.</param>
    public void Kick(Guid memberId) => _coop.Kick(memberId);

    /// <summary>Picks the inventory stack to deposit.</summary>
    /// <param name="slot">The inventory slot.</param>
    public void PickDeposit(int slot)
    {
        _depositSlot = slot;
        foreach (SlotDisplay s in _inventory.Slots)
        {
            if (s.Index == slot && !s.IsEmpty)
            {
                this.QuantityText = s.Quantity.ToString(CultureInfo.InvariantCulture);
            }
        }

        this.Raise();
    }

    /// <summary>Picks the warehouse item to withdraw.</summary>
    /// <param name="itemId">The item id.</param>
    public void PickWithdraw(int itemId)
    {
        _withdrawItem = itemId;
        foreach (CoopStockInfo row in _coop.Current.Stock)
        {
            if (row.ItemId == itemId)
            {
                this.QuantityText = Math.Max(1, row.Available).ToString(CultureInfo.InvariantCulture);
            }
        }

        this.Raise();
    }

    /// <summary>Deposits the picked stack in the typed quantity.</summary>
    public void Deposit()
    {
        foreach (SlotDisplay slot in _inventory.Slots)
        {
            if (slot.Index == _depositSlot && !slot.IsEmpty && int.TryParse(this.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
            {
                _coop.Deposit(slot.ItemId, quantity);
                return;
            }
        }

        this.SetStatus(this.Text("Coop_Status_InvalidQuantity"));
    }

    /// <summary>Withdraws the picked item in the typed quantity.</summary>
    public void Withdraw()
    {
        if (_withdrawItem != 0 && int.TryParse(this.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
        {
            _coop.Withdraw(_withdrawItem, quantity);
            return;
        }

        this.SetStatus(this.Text("Coop_Status_InvalidQuantity"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _coop.Changed -= this.Raise;
        _coop.Completed -= this.OnCompleted;
        _nearby.Changed -= this.Raise;
        _inventory.Changed -= this.Raise;
    }

    private void OnCompleted(CoopStatus status) => this.SetStatus(status == CoopStatus.Ok ? string.Empty : this.Text("Coop_Status_" + status));
}
