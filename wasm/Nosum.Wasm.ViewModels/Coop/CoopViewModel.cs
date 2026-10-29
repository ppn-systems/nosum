// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Coop;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Text;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Player;
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

/// <summary>One price line of an upgrade.</summary>
/// <param name="ItemName">The localized item name.</param>
/// <param name="Needed">How many the upgrade takes from the warehouse.</param>
/// <param name="Have">How many the warehouse holds.</param>
public sealed record UpgradeCostDisplay(string ItemName, int Needed, int Have)
{
    /// <summary>Gets whether the warehouse holds enough of the item.</summary>
    public bool Enough => this.Have >= this.Needed;
}

/// <summary>One upgrade track of the co-op panel.</summary>
/// <param name="Track">The track.</param>
/// <param name="Title">The localized track name.</param>
/// <param name="Level">The level now.</param>
/// <param name="Summary">What the level gives, localized.</param>
/// <param name="LevelText">"Level 1 / 3", localized.</param>
/// <param name="IsMax">Whether the track is complete.</param>
/// <param name="Costs">The price of the next level; empty when complete.</param>
/// <param name="CanBuy">Whether the player is the founder and the warehouse holds the whole price.</param>
public sealed record UpgradeDisplay(CoopUpgradeTrack Track, string Title, int Level, string Summary, string LevelText, bool IsMax, IReadOnlyList<UpgradeCostDisplay> Costs, bool CanBuy)
{
    /// <summary>Gets the next level along this track.</summary>
    public int NextLevel => this.Level + 1;
}

/// <summary>One shared objective of the co-op panel (docs/adr/ADR-067).</summary>
/// <param name="Title">The localized objective, named after the crop.</param>
/// <param name="Contributed">How many the members have harvested toward it.</param>
/// <param name="Target">How many complete it.</param>
/// <param name="Completed">Whether the co-op has earned the trophy.</param>
public sealed record ObjectiveDisplay(string Title, int Contributed, int Target, bool Completed)
{
    /// <summary>Gets the progress as a fraction of the target, at most one.</summary>
    public double Fraction => Math.Min(1.0, (double)this.Contributed / this.Target);
}

/// <summary>View model of the co-op panel: found a co-op, answer invitations, invite players on the map, manage members, and use the shared warehouse. The server decides every outcome, including who may withdraw what.</summary>
public sealed class CoopViewModel : PanelViewModel, IDisposable
{
    private readonly CoopChannel _coop;
    private readonly NearbyPlayers _nearby;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private readonly NicknameFormatter _names;
    private int _depositSlot = -1;
    private int _withdrawItem;

    /// <summary>Initializes the view model.</summary>
    /// <param name="coop">The co-op channel.</param>
    /// <param name="nearby">The other players on the map (invitations).</param>
    /// <param name="inventory">The inventory view model (what can be deposited).</param>
    /// <param name="describer">Describes warehouse items.</param>
    /// <param name="names">Names the players by nickname.</param>
    /// <param name="text">UI text.</param>
    public CoopViewModel(CoopChannel coop, NearbyPlayers nearby, InventoryViewModel inventory, ItemDescriber describer, NicknameFormatter names, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(coop);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(names);

        _coop = coop;
        _nearby = nearby;
        _inventory = inventory;
        _describer = describer;
        _names = names;
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
    public IReadOnlyList<(Guid Id, string Name, string Inviter)> Invites
    {
        get
        {
            List<(Guid, string, string)> invites = [];
            string tag = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            foreach (CoopInviteInfo invite in _coop.Current.Invites)
            {
                invites.Add((invite.InviteId, LocalizedText.Resolve(invite.CoopName, tag), _names.Display(invite.InviterNickname)));
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
                string nickname = _names.Display(member.Nickname);
                string label = member.IsMe ? this.Text("Coop_You") : nickname.Length > 0 ? nickname : this.Text("Coop_MemberNumber", number);
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
                players.Add(new PlayerChoice(id, _names.PlayerLabel(id)));
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

    /// <summary>Gets the two upgrade tracks (docs/adr/ADR-038) with the price of the next level against what the warehouse holds. The server decides the purchase.</summary>
    public IReadOnlyList<UpgradeDisplay> Upgrades
    {
        get
        {
            CoopInfo info = _coop.Current;
            return
            [
                this.UpgradeOf(CoopUpgradeTrack.Warehouse, CoopTrack.Warehouse, info.WarehouseLevel, this.Text("Coop_Upgrade_Warehouse_Now", CoopUpgrades.WarehouseSlots(info.WarehouseLevel), info.Stock.Count)),
                this.UpgradeOf(CoopUpgradeTrack.Land, CoopTrack.Land, info.LandLevel, this.Text("Coop_Upgrade_Land_Now", info.LandLevel + 1, CoopUpgrades.MaxLevel + 1)),
            ];
        }
    }

    /// <summary>Gets the shared objectives with the co-op's progress (docs/adr/ADR-067). The completed ones are the co-op's trophies; the server counts the harvests.</summary>
    public IReadOnlyList<ObjectiveDisplay> Objectives
    {
        get
        {
            IReadOnlyList<CoopObjectiveInfo> progress = _coop.Current.Objectives ?? [];
            List<ObjectiveDisplay> rows = [];
            foreach (CoopObjectiveDefinition objective in CoopObjectiveCatalog.All)
            {
                int contributed = 0;
                bool completed = false;
                foreach (CoopObjectiveInfo info in progress)
                {
                    if (info.ObjectiveId == objective.Id)
                    {
                        contributed = info.Contributed;
                        completed = info.Completed;
                        break;
                    }
                }

                rows.Add(new ObjectiveDisplay(this.Text("Coop_Objective_Harvest", _describer.NameOf(objective.ItemId)), contributed, objective.Target, completed));
            }

            return rows;
        }
    }

    /// <summary>Gets the warehouse log lines as localized text, newest first.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            List<string> lines = [];
            Dictionary<Guid, string> names = [];
            int n = 0;
            foreach (CoopMemberInfo member in _coop.Current.Members)
            {
                n++;
                string nickname = _names.Display(member.Nickname);
                names[member.MemberId] = nickname.Length > 0 ? nickname : this.Text("Coop_MemberNumber", n);
            }

            foreach (CoopLogInfo entry in _coop.Current.Log)
            {
                string who = entry.MemberId == Guid.Empty || !names.TryGetValue(entry.MemberId, out string? known) ? this.Text("Coop_FormerMember") : known;
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

    /// <summary>Buys the next level of a track (the server refuses anyone but the founder).</summary>
    /// <param name="track">The track.</param>
    public void Upgrade(CoopUpgradeTrack track)
    {
        int level = track == CoopUpgradeTrack.Land ? _coop.Current.LandLevel : _coop.Current.WarehouseLevel;
        _coop.Upgrade(track, level + 1);
    }

    /// <summary>Buys the next level of the given upgrade track display.</summary>
    /// <param name="upgrade">The upgrade track display.</param>
    public void Upgrade(UpgradeDisplay upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        _coop.Upgrade(upgrade.Track, upgrade.NextLevel);
    }

    /// <summary>Buys the next level of the given upgrade track display.</summary>
    /// <param name="upgrade">The upgrade track display.</param>
    /// <returns>A completed task.</returns>
    public Task UpgradeAsync(UpgradeDisplay upgrade)
    {
        this.Upgrade(upgrade);
        return Task.CompletedTask;
    }

    /// <summary>Accepts an invitation to join a co-op.</summary>
    /// <param name="inviteId">The invitation id.</param>
    public void AcceptInvite(Guid inviteId) => this.Respond(inviteId, accept: true);

    /// <summary>Declines an invitation to join a co-op.</summary>
    /// <param name="inviteId">The invitation id.</param>
    public void DeclineInvite(Guid inviteId) => this.Respond(inviteId, accept: false);

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

    private UpgradeDisplay UpgradeOf(CoopUpgradeTrack wire, CoopTrack track, int level, string summary)
    {
        bool isMax = level >= CoopUpgrades.MaxLevel;
        List<UpgradeCostDisplay> costs = [];
        bool enough = !isMax;
        if (!isMax)
        {
            foreach (UpgradeCost cost in CoopUpgrades.Cost(track, level + 1))
            {
                int have = 0;
                foreach (CoopStockInfo row in _coop.Current.Stock)
                {
                    if (row.ItemId == cost.ItemId)
                    {
                        have = row.Quantity;
                    }
                }

                costs.Add(new UpgradeCostDisplay(_describer.NameOf(cost.ItemId), cost.Quantity, have));
                enough &= have >= cost.Quantity;
            }
        }

        return new UpgradeDisplay(wire, this.Text(track == CoopTrack.Land ? "Coop_Upgrade_Land" : "Coop_Upgrade_Warehouse"), level, summary, this.Text("Coop_Upgrade_Level", level, CoopUpgrades.MaxLevel), isMax, costs, this.IsFounder && enough);
    }

    private void OnCompleted(CoopStatus status) => this.SetStatus(status == CoopStatus.Ok ? string.Empty : this.Text("Coop_Status_" + status));
}
