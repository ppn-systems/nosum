// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Npc;

/// <summary>Where a conversation with a merchant is (docs/adr/ADR-043 D3).</summary>
public enum NpcStage
{
    /// <summary>No conversation.</summary>
    Closed = 0,

    /// <summary>The dialog box is open at the bottom of the screen: portrait, name, greeting and the action buttons.</summary>
    Speaking = 1,

    /// <summary>A standalone dialog opened from the box is showing (<see cref="NpcDialogViewModel.Page"/>).</summary>
    Trading = 2,
}

/// <summary>The page the standalone dialog shows while <see cref="NpcStage.Trading"/>.</summary>
public enum NpcPage
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>What the counter's shop sells.</summary>
    Shop = 1,

    /// <summary>The carpenter's buildings and animals.</summary>
    Build = 2,

    /// <summary>The blacksmith's tool upgrades.</summary>
    Upgrade = 3,

    /// <summary>The market's listings, to buy.</summary>
    MarketBuy = 4,

    /// <summary>The market's page for listing a stack.</summary>
    MarketSell = 5,

    /// <summary>The Town board's goals and requests.</summary>
    Goals = 6,
}

/// <summary>One button of the dialog box.</summary>
/// <param name="Action">What it asks.</param>
/// <param name="Label">The localized label.</param>
public sealed record NpcChoice(NpcActions Action, string Label);

/// <summary>
/// View model of the merchants' dialog (docs/adr/ADR-043). The action key at a counter (or a click on the merchant) opens a dialog box; its buttons open the
/// shop, market, carpenter, blacksmith or board page as a dialog of their own, never as a tab of the game menu. Nothing here is sent to the server: the
/// merchant is how the client presents a counter, and the server still checks where the player stands for every purchase.
/// </summary>
public sealed class NpcDialogViewModel : PanelViewModel
{
    // The order of the buttons; Talk comes last before Leave.
    private static readonly NpcActions[] s_order = [NpcActions.Buy, NpcActions.Sell, NpcActions.Build, NpcActions.Upgrade, NpcActions.Goals, NpcActions.Talk];

    private int _talkIndex;
    private bool _talking;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    public NpcDialogViewModel(IStringLocalizer<Language> text)
        : base(text)
    {
    }

    /// <summary>Gets the merchant being spoken to, or <see langword="null"/> when no conversation is open.</summary>
    public NpcDefinition? Npc { get; private set; }

    /// <summary>Gets where the conversation is.</summary>
    public NpcStage Stage { get; private set; }

    /// <summary>Gets the page of the standalone dialog; <see cref="NpcPage.None"/> unless <see cref="Stage"/> is <see cref="NpcStage.Trading"/>.</summary>
    public NpcPage Page { get; private set; }

    /// <summary>Gets the merchant's name, localized.</summary>
    public string Name => this.Npc is null ? string.Empty : this.Text(this.Npc.TextKey("Name"));

    /// <summary>Gets what the merchant does ("Seed shop keeper"), localized.</summary>
    public string Role => this.Npc is null ? string.Empty : this.Text(this.Npc.TextKey("Role"));

    /// <summary>Gets the line the merchant says now: the greeting, or the last thing said by Talk.</summary>
    public string Line => this.Npc is null ? string.Empty : this.Text(this.Npc.TextKey(_talking ? (_talkIndex == 0 ? "Talk1" : "Talk2") : "Greeting"));

    /// <summary>Gets the portrait's picture, a path under the app root.</summary>
    public string PortraitUrl => this.Npc is null ? string.Empty : "assets/sprites/npcs/" + this.Npc.PortraitKey + ".png";

    /// <summary>Gets the text of the Leave button.</summary>
    public string LeaveLabel => this.Text("Npc_Leave");

    /// <summary>Gets the buttons the merchant offers, besides Leave.</summary>
    public IReadOnlyList<NpcChoice> Choices
    {
        get
        {
            List<NpcChoice> choices = [];
            if (this.Npc is null)
            {
                return choices;
            }

            foreach (NpcActions action in s_order)
            {
                if ((this.Npc.Actions & action) != 0)
                {
                    choices.Add(new NpcChoice(action, this.Text("Npc_Action_" + action)));
                }
            }

            return choices;
        }
    }

    /// <summary>Gets the shop the standalone dialog shows (for <see cref="NpcPage.Shop"/>, <see cref="NpcPage.Build"/> and <see cref="NpcPage.Upgrade"/>), or zero.</summary>
    public int ShopId => this.Page is NpcPage.Shop or NpcPage.Build or NpcPage.Upgrade && this.Npc is not null ? this.Npc.CounterId : 0;

    /// <summary>Gets the panel the standalone dialog hosts: the shop, market or board page, or <see cref="PanelKind.None"/> when no page is showing.</summary>
    public PanelKind Panel => this.Page switch
    {
        NpcPage.Shop or NpcPage.Build or NpcPage.Upgrade => PanelKind.Shop,
        NpcPage.MarketBuy or NpcPage.MarketSell => PanelKind.Market,
        NpcPage.Goals => PanelKind.Goals,
        _ => PanelKind.None,
    };

    /// <summary>Gets the title of the standalone dialog, localized: the shop's name, the market or the board.</summary>
    public string PageTitle => this.Page switch
    {
        NpcPage.Shop or NpcPage.Build or NpcPage.Upgrade => ShopCatalog.TryGet(this.ShopId, out ShopDefinition? shop) ? this.Text("Shop_Title_" + shop.Key) : string.Empty,
        NpcPage.MarketBuy or NpcPage.MarketSell => this.Text("Toolbar_Market"),
        NpcPage.Goals => this.Text("Toolbar_Goals"),
        _ => string.Empty,
    };

    /// <summary>Opens the dialog box of the merchant who serves a counter.</summary>
    /// <param name="counterId">The counter the action key was pressed at (or whose merchant was clicked).</param>
    /// <returns><see langword="true"/> when a conversation was opened; <see langword="false"/> when one is already open or no merchant serves that counter.</returns>
    public bool Interact(int counterId)
    {
        if (this.Stage != NpcStage.Closed || !NpcCatalog.TryGetByCounter(counterId, out NpcDefinition? npc))
        {
            return false;
        }

        this.Npc = npc;
        this.Stage = NpcStage.Speaking;
        this.Page = NpcPage.None;
        _talking = false;
        _talkIndex = 0;
        this.Raise();
        return true;
    }

    /// <summary>Does what a button asks.</summary>
    /// <param name="action">The button. Ignored when the merchant does not offer it.</param>
    public void Choose(NpcActions action)
    {
        if (this.Npc is null || this.Stage == NpcStage.Closed || (this.Npc.Actions & action) == 0)
        {
            return;
        }

        switch (action)
        {
            case NpcActions.Talk:
                this.Stage = NpcStage.Speaking;
                this.Page = NpcPage.None;
                _talkIndex = _talking ? 1 - _talkIndex : 0;
                _talking = true;
                break;
            case NpcActions.Buy:
                this.Show(this.Npc.CounterId == MarketRules.MarketCounterId ? NpcPage.MarketBuy : NpcPage.Shop);
                break;
            case NpcActions.Sell:
                this.Show(NpcPage.MarketSell);
                break;
            case NpcActions.Build:
                this.Show(NpcPage.Build);
                break;
            case NpcActions.Upgrade:
                this.Show(NpcPage.Upgrade);
                break;
            case NpcActions.Goals:
                this.Show(NpcPage.Goals);
                break;
            default:
                return;
        }

        this.Raise();
    }

    /// <summary>Goes back from the standalone dialog to the dialog box.</summary>
    public void Back()
    {
        if (this.Stage != NpcStage.Trading)
        {
            return;
        }

        this.Stage = NpcStage.Speaking;
        this.Page = NpcPage.None;
        _talking = false;
        this.Raise();
    }

    /// <summary>Ends the conversation.</summary>
    public void Leave()
    {
        if (this.Stage == NpcStage.Closed)
        {
            return;
        }

        this.Npc = null;
        this.Stage = NpcStage.Closed;
        this.Page = NpcPage.None;
        _talking = false;
        this.Raise();
    }

    /// <summary>The farmer walked out of reach of a counter: the conversation with its merchant ends.</summary>
    /// <param name="counterId">The counter that was left.</param>
    public void CounterLeft(int counterId)
    {
        if (this.Npc is not null && this.Npc.CounterId == counterId)
        {
            this.Leave();
        }
    }

    private void Show(NpcPage page)
    {
        this.Stage = NpcStage.Trading;
        this.Page = page;
    }
}
