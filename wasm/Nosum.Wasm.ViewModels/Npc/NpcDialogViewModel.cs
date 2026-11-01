// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Quest;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Gameplay.Weather;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Mine;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;

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

    /// <summary>The player's wardrobe: looks and clothes, from the tailor (docs/adr/ADR-081).</summary>
    Wardrobe = 7,

    /// <summary>The co-op page, from the board keeper (docs/adr/ADR-081).</summary>
    Coop = 8,

    /// <summary>The backpack page: the next size and its price, from the tailor (docs/adr/ADR-081).</summary>
    Backpack = 9,
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
public sealed class NpcDialogViewModel : PanelViewModel, IDisposable
{
    /// <summary>How long one character of a line takes to appear (issue #214).</summary>
    public static readonly TimeSpan RevealInterval = TimeSpan.FromMilliseconds(30);

    // The order of the buttons; Talk comes last before Leave.
    private static readonly NpcActions[] s_order =
    [
        NpcActions.Buy,
        NpcActions.Sell,
        NpcActions.Build,
        NpcActions.Upgrade,
        NpcActions.Goals,
        NpcActions.Coop,
        NpcActions.Wardrobe,
        NpcActions.Backpack,
        NpcActions.Talk,
    ];

    private readonly IStringLocalizer<Language> _localizer;
    private readonly CalendarChannel _calendar;
    private readonly QuestChannel _quests;
    private readonly MineChannel _mine;
    private readonly SettingsViewModel? _settings;
    private readonly Timer _reveal;

    // How many lines the conversation has had: zero for the greeting, one more for each Talk (docs/adr/ADR-080 D2).
    private int _slot;
    private string _lineText = string.Empty;
    private int _revealed;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="calendar">The server calendar: the day, season, weather and hour a line may mention.</param>
    /// <param name="quests">The player's quests: whether this is a new farmer, and whether the first harvest is in.</param>
    /// <param name="mine">The player's mine progress: the deepest floor reached.</param>
    /// <param name="settings">The player's settings: with reduced motion on, a line appears at once instead of typing out. None means the full typewriter.</param>
    public NpcDialogViewModel(IStringLocalizer<Language> text, CalendarChannel calendar, QuestChannel quests, MineChannel mine, SettingsViewModel? settings = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(mine);

        _localizer = text;
        _calendar = calendar;
        _quests = quests;
        _mine = mine;
        _settings = settings;
        _reveal = new Timer(_ => this.RevealNext(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Gets the merchant being spoken to, or <see langword="null"/> when no conversation is open.</summary>
    public NpcDefinition? Npc { get; private set; }

    /// <summary>Gets where the conversation is.</summary>
    public NpcStage Stage { get; private set; }

    /// <summary>Gets whether the merchant dialog box is currently speaking at the bottom of the screen.</summary>
    public bool IsSpeaking => this.Stage == NpcStage.Speaking;

    /// <summary>Gets whether a standalone trading dialog opened from the merchant is currently showing.</summary>
    public bool IsTrading => this.Stage == NpcStage.Trading;

    /// <summary>Gets the page of the standalone dialog; <see cref="NpcPage.None"/> unless <see cref="Stage"/> is <see cref="NpcStage.Trading"/>.</summary>
    public NpcPage Page { get; private set; }

    /// <summary>Gets whether the standalone dialog is showing a shop, carpenter, or blacksmith page.</summary>
    public bool IsShopPage => this.Page is NpcPage.Shop or NpcPage.Build or NpcPage.Upgrade;

    /// <summary>Gets whether the standalone dialog is showing the market buy page.</summary>
    public bool IsMarketBuyPage => this.Page == NpcPage.MarketBuy;

    /// <summary>Gets whether the standalone dialog is showing the market sell page.</summary>
    public bool IsMarketSellPage => this.Page == NpcPage.MarketSell;

    /// <summary>Gets whether the standalone dialog is showing the Town board goals page.</summary>
    public bool IsGoalsPage => this.Page == NpcPage.Goals;

    /// <summary>Gets whether the standalone dialog is showing the backpack page (the tailor's next size).</summary>
    public bool IsBackpackPage => this.Page == NpcPage.Backpack;

    /// <summary>Gets the merchant's name, localized.</summary>
    public string Name => this.Npc is null ? string.Empty : this.Text(this.Npc.TextKey("Name"));

    /// <summary>Gets what the merchant does ("Seed shop keeper"), localized.</summary>
    public string Role => this.Npc is null ? string.Empty : this.Text(this.Npc.TextKey("Role"));

    /// <summary>Gets the part of the line the merchant has said so far: the greeting, or the last thing said by Talk, typed out a character at a time.</summary>
    public string Line => _lineText[.._revealed];

    /// <summary>Gets whether the line is still typing out. The choices wait until it is done; a click on the line finishes it (<see cref="Skip"/>).</summary>
    public bool IsRevealing => _revealed < _lineText.Length;

    /// <summary>Gets whether the portrait speaks (bobs) while the line types out. Off with reduced motion, where the line is never typed.</summary>
    public bool PortraitTalking => this.IsRevealing;

    /// <summary>Gets the portrait's picture, a path under the app root.</summary>
    public string PortraitUrl => this.Npc is null ? string.Empty : "assets/sprites/npcs/" + this.Npc.PortraitKey + ".png";

    /// <summary>Gets the text of the Leave button.</summary>
    public string LeaveLabel => this.Text("Npc_Leave");

    /// <summary>Gets the accessible name and tooltip of the close cross in the dialog box's title bar: the same as Leave.</summary>
    public string CloseLabel => this.Text("Panel_Close");

    /// <summary>Gets the buttons the merchant offers, besides Leave. None while the line is still typing out.</summary>
    public IReadOnlyList<NpcChoice> Choices
    {
        get
        {
            List<NpcChoice> choices = [];
            if (this.Npc is null || this.IsRevealing)
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
        NpcPage.Wardrobe => PanelKind.Wardrobe,
        NpcPage.Coop => PanelKind.Coop,
        NpcPage.Backpack => PanelKind.Backpack,
        _ => PanelKind.None,
    };

    /// <summary>Gets the title of the standalone dialog, localized: the shop's name, the market or the board.</summary>
    public string PageTitle => this.Page switch
    {
        NpcPage.Shop or NpcPage.Build or NpcPage.Upgrade => ShopCatalog.TryGet(this.ShopId, out ShopDefinition? shop) ? this.Text("Shop_Title_" + shop.Key) : string.Empty,
        NpcPage.MarketBuy or NpcPage.MarketSell => this.Text("Toolbar_Market"),
        NpcPage.Goals => this.Text("Toolbar_Goals"),
        NpcPage.Wardrobe => this.Text("Toolbar_Wardrobe"),
        NpcPage.Coop => this.Text("Toolbar_Coop"),
        NpcPage.Backpack => this.Text("Toolbar_Backpack"),
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
        _slot = 0;
        this.SpeakCurrentLine();
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
                _slot++;
                this.SpeakCurrentLine();
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
            case NpcActions.Coop:
                this.Show(NpcPage.Coop);
                break;
            case NpcActions.Wardrobe:
                this.Show(NpcPage.Wardrobe);
                break;
            case NpcActions.Backpack:
                this.Show(NpcPage.Backpack);
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
        _slot = 0;
        this.SpeakCurrentLine();
        this.Raise();
    }

    /// <summary>Finishes the line at once, so the choices show (a click on the line).</summary>
    public void Skip()
    {
        if (!this.IsRevealing)
        {
            return;
        }

        _reveal.Change(Timeout.Infinite, Timeout.Infinite);
        _revealed = _lineText.Length;
        this.Raise();
    }

    /// <summary>Ends the conversation.</summary>
    public void Leave()
    {
        if (this.Stage == NpcStage.Closed)
        {
            return;
        }

        _reveal.Change(Timeout.Infinite, Timeout.Infinite);
        _lineText = string.Empty;
        _revealed = 0;
        this.Npc = null;
        this.Stage = NpcStage.Closed;
        this.Page = NpcPage.None;
        _slot = 0;
        this.Raise();
    }

    /// <inheritdoc/>
    public void Dispose() => _reveal.Dispose();

    /// <summary>The farmer walked out of reach of a counter: the conversation with its merchant ends.</summary>
    /// <param name="counterId">The counter that was left.</param>
    public void CounterLeft(int counterId)
    {
        if (this.Npc is not null && this.Npc.CounterId == counterId)
        {
            this.Leave();
        }
    }

    // Starts the line the merchant now says. With reduced motion it appears whole; otherwise one character per RevealInterval.
    private void SpeakCurrentLine()
    {
        _reveal.Change(Timeout.Infinite, Timeout.Infinite);
        _lineText = this.CurrentLine();
        _revealed = _lineText.Length;
        if (_lineText.Length > 0 && this.Npc is not null && _settings?.ReducesMotion != true)
        {
            _revealed = 0;
            _reveal.Change(RevealInterval, RevealInterval);
        }
    }

    private void RevealNext()
    {
        if (!this.IsRevealing)
        {
            _reveal.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _revealed++;
        if (!this.IsRevealing)
        {
            _reveal.Change(Timeout.Infinite, Timeout.Infinite);
        }

        this.Raise();
    }

    private string CurrentLine() => this.Npc is null
        ? string.Empty
        : this.Text(NpcGreeting.KeyFor(this.Npc, this.Moment(), _slot, this.HasText));

    private void Show(NpcPage page)
    {
        this.Stage = NpcStage.Trading;
        this.Page = page;
    }

    // The moment now: the day, season, weather and hour while the calendar is known, and the farmer's progress either way.
    private NpcLineContext Moment()
    {
        bool newcomer = true;
        bool harvested = false;
        IReadOnlyList<QuestInfo> quests = _quests.Quests();
        foreach (QuestInfo quest in quests)
        {
            if (quest.State == QuestState.Completed)
            {
                newcomer = false;
                harvested |= quest.QuestId == QuestCatalog.FirstHarvest;
            }
        }

        string? activeQuest = Nosum.Wasm.ViewModels.Quest.ActiveQuest.Of(quests)?.Key;
        bool deepMiner = _mine.DeepestFloor >= NpcLineContext.DeepMinerFloor;
        if (!_calendar.IsLoaded)
        {
            return NpcLineContext.Of(0, null, null, null, newcomer, harvested, deepMiner, activeQuest);
        }

        GameCalendar calendar = new(_calendar.EpochMs, _calendar.DayLengthMs);
        long now = _calendar.ServerNowMs();
        return NpcLineContext.Of(
            calendar.DayNumberAt(now),
            calendar.SeasonAt(now),
            WeatherRules.At(calendar, now),
            calendar.At(now).Hour,
            newcomer,
            harvested,
            deepMiner,
            activeQuest);
    }

    private bool HasText(string key) => !_localizer[key].ResourceNotFound;
}
