// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Blacksmith;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Blacksmith;

/// <summary>One tool the player carries that the blacksmith can upgrade, already localized.</summary>
/// <param name="ToolItemId">The tool to hand in.</param>
/// <param name="From">The tool as it is now, for the slot control.</param>
/// <param name="To">The tool as it will be.</param>
/// <param name="CostText">What it costs ("2,000 coins + 5 Copper bar").</param>
/// <param name="CanUpgrade">Whether the smith is free and the player has the coins and the bars.</param>
public sealed record UpgradeOfferDisplay(int ToolItemId, SlotDisplay From, SlotDisplay To, string CostText, bool CanUpgrade);

/// <summary>The tool the smith is making for the player.</summary>
/// <param name="Tool">The upgraded tool, for the slot control.</param>
/// <param name="StatusText">"Ready in 2 game days" or "Ready! Collect it."</param>
/// <param name="CanCollect">Whether the two game days are over.</param>
public sealed record SmithJobDisplay(SlotDisplay Tool, string StatusText, bool CanCollect);

/// <summary>
/// View model of the blacksmith's upgrade section (docs/adr/ADR-026 D2), shown on the blacksmith's shop tab. It lists the upgradable tools the player carries with
/// their price from the shared <see cref="BlacksmithRules"/>, hands a tool in, shows what the smith is making and collects it. The server decides every request.
/// </summary>
public sealed class BlacksmithViewModel : PanelViewModel, IDisposable
{
    private static readonly ToolKind[] s_tools = [ToolKind.Hoe, ToolKind.WateringCan, ToolKind.Axe, ToolKind.Pickaxe];

    private readonly BlacksmithChannel _channel;
    private readonly InventoryChannel _inventory;
    private readonly WalletChannel _wallet;
    private readonly ItemDescriber _describer;
    private BlacksmithInfo _info;
    private string _after = string.Empty;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Talks to the blacksmith.</param>
    /// <param name="inventory">What the player carries.</param>
    /// <param name="wallet">The coin balance.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    public BlacksmithViewModel(BlacksmithChannel channel, InventoryChannel inventory, WalletChannel wallet, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(describer);
        _channel = channel;
        _inventory = inventory;
        _wallet = wallet;
        _describer = describer;
        channel.Updated += this.OnUpdated;
        inventory.Changed += this.Raise;
        wallet.Changed += this.Raise;
    }

    /// <summary>Gets the line that explains what the smith does and how long it takes.</summary>
    public string HintText => this.Text("Smith_Hint", BlacksmithRules.UpgradeDays);

    /// <summary>Gets the tool the smith is making, or <see langword="null"/> when the smith holds nothing of the player's.</summary>
    public SmithJobDisplay? Job
    {
        get
        {
            if (_info.ToolItemId == 0)
            {
                return null;
            }

            long left = _info.ReadyDay - _info.Today;
            string status = left <= 0 ? this.Text("Smith_Ready") : left == 1 ? this.Text("Smith_ReadyTomorrow") : this.Text("Smith_ReadyIn", left);
            return new SmithJobDisplay(_describer.Describe(0, _info.ToolItemId, 1), status, left <= 0);
        }
    }

    /// <summary>Gets the upgrades the player can ask for: the best tier they carry of each upgradable tool that is not at the top tier yet.</summary>
    public IReadOnlyList<UpgradeOfferDisplay> Offers
    {
        get
        {
            List<UpgradeOfferDisplay> offers = [];
            foreach (ToolKind kind in s_tools)
            {
                int carried = BestCarried(kind);
                if (carried == 0 || !BlacksmithRules.TryQuote(carried, out UpgradeQuote? found))
                {
                    continue;
                }

                UpgradeQuote quote = found.Value;
                bool affordable = _wallet.Coins >= quote.Coins && _inventory.CountOf(quote.BarItemId) >= quote.BarCount;
                offers.Add(new UpgradeOfferDisplay(
                    quote.FromItemId,
                    _describer.Describe(0, quote.FromItemId, 1),
                    _describer.Describe(0, quote.ToItemId, 1),
                    this.Text("Smith_Cost", Number(quote.Coins), quote.BarCount, _describer.NameOf(quote.BarItemId)),
                    affordable && _info.ToolItemId == 0));
            }

            return offers;
        }
    }

    /// <summary>Reads what the smith holds (call when the blacksmith's tab opens).</summary>
    public void Open()
    {
        this.ClearStatus();
        _channel.Load();
        this.Raise();
    }

    /// <summary>Hands a tool in.</summary>
    /// <param name="toolItemId">The tool.</param>
    public void Upgrade(int toolItemId)
    {
        this.ClearStatus();
        _after = "Smith_Upgraded";
        _channel.Upgrade(toolItemId);
    }

    /// <summary>Collects the upgraded tool.</summary>
    public void Collect()
    {
        this.ClearStatus();
        _after = "Smith_Collected";
        _channel.Collect();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Updated -= this.OnUpdated;
        _inventory.Changed -= this.Raise;
        _wallet.Changed -= this.Raise;
    }

    private void OnUpdated(BlacksmithInfo info)
    {
        _info = info;
        string key = _after;
        _after = string.Empty;
        if (info.Status != BlacksmithStatus.Ok)
        {
            this.SetStatus(this.Text("Smith_Status_" + info.Status));
        }
        else if (key.Length > 0)
        {
            this.SetStatus(this.Text(key), good: true);
        }
        else
        {
            this.Raise();
        }
    }

    // The tool of a kind the player carries at the highest tier, or zero.
    private int BestCarried(ToolKind kind)
    {
        int best = 0;
        ToolTier bestTier = ToolTier.Basic;
        for (int slot = 0; slot < _inventory.Capacity; slot++)
        {
            SlotInfo info = _inventory.SlotAt(slot);
            if (info.ItemId != 0 && ItemCatalog.TryGet(info.ItemId, out ItemDefinition? item) && item.Kind == ItemKind.Tool && item.Tool == kind && (best == 0 || item.Tier > bestTier))
            {
                best = item.Id;
                bestTier = item.Tier;
            }
        }

        return best;
    }
}
