// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels;

namespace Nosum.Wasm.Game;

/// <summary>
/// What the player's one-shot keys do: number keys select a hotbar slot, the action key (E or Space) uses the
/// held item on the tile in front of the farmer (or ships it when that tile is the shipping bin, or eats it when it is food and the player has room for energy), I M C T G H N open the panels, Escape closes them; next to a shop the action key opens it, next to the mine elevator it opens the elevator picker.
/// The rotate key (R) is the wide use of an upgraded hoe or watering can: one swing over a line of three or five tiles (docs/adr/ADR-026 D1). The scene calls <see cref="Update"/> once per
/// frame; the tile in front is kept in <see cref="TargetX"/> and <see cref="TargetY"/> for the cursor the farm decorator draws.
/// Allocation-free; the requests themselves are sent by the channels, off the frame.
/// </summary>
internal sealed partial class PlayerActions
{
    // A cooldown just over the server's 300 ms swing keeps a mashed key from being refused (docs/adr/ADR-021 D8) and inside its rate limit.
    private const double UseCooldownSeconds = 0.32;
    private const double SwingDurationSeconds = 0.30;

    // A drag sends one farm use per tile it reaches, at most this often, so a fast drag makes a steady line of tiles and not a flood.
    private const double DragStepSeconds = 0.12;

    private readonly FarmChannel _farm;
    private readonly InventoryChannel _inventory;
    private readonly MarketChannel _market;
    private int _counterId;
    private readonly ChestChannel? _chests;
    private readonly ProgressChannel? _progress;
    private readonly BuildingChannel? _buildings;
    private readonly GameAudio? _audio;
    private readonly CombatChannel? _combat;
    private readonly CalendarChannel? _calendar;
    private GameCalendar? _rules;
    private double _lastUseSeconds = double.NegativeInfinity;
    private double _swingStartSeconds = double.NegativeInfinity;
    private int _swingItemId;
    private int _dragTileX;
    private int _dragTileY;
    private bool _dragging;
    private double _lastDragSeconds = double.NegativeInfinity;

    /// <summary>Initializes the actions.</summary>
    /// <param name="farm">Sends the use of an item.</param>
    /// <param name="inventory">Holds the selected slot.</param>
    /// <param name="market">Sends the shipping of the held stack at the bin.</param>
    /// <param name="progress">Sends eating when the action key is pressed with food in hand; <see langword="null"/> to never eat.</param>
    /// <param name="chests">Opens the chest the farmer faces; <see langword="null"/> when chests are not wired.</param>
    /// <param name="buildings">Sends feeding, petting and collecting inside a coop or a barn; <see langword="null"/> when buildings are not wired.</param>
    /// <param name="audio">Plays the swing of the tool in hand (docs/adr/ADR-039); <see langword="null"/> for no sound.</param>
    /// <param name="combat">Sends monster attacks and potion drinking; <see langword="null"/> when combat is not wired.</param>
    /// <param name="calendar">The server calendar, so withered crops do not block eating or drinking potions.</param>
    public PlayerActions(FarmChannel farm, InventoryChannel inventory, MarketChannel market, ProgressChannel? progress = null, ChestChannel? chests = null, BuildingChannel? buildings = null, GameAudio? audio = null, CombatChannel? combat = null, CalendarChannel? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(market);
        _farm = farm;
        _inventory = inventory;
        _market = market;
        _chests = chests;
        _progress = progress;
        _buildings = buildings;
        _audio = audio;
        _combat = combat;
        _calendar = calendar;
    }

    /// <summary>Raised (from the frame) when a panel key was pressed: the panel to show or hide.</summary>
    public event Action<PanelKind>? PanelToggleRequested;

    /// <summary>Raised (from the frame) when the action key was pressed next to a shop: the shop's id (docs/adr/ADR-013 amendments).</summary>
    public event Action<int>? ShopRequested;

    /// <summary>Raised (from the frame) when the farmer walked out of reach of a counter: the counter's id (a shop, the Town board or the market, docs/adr/ADR-034), so its panel can close.</summary>
    public event Action<int>? CounterLeft;

    /// <summary>Raised (from the frame) when the action key was pressed next to the mine elevator (docs/adr/ADR-026 D3).</summary>
    public event Action? ElevatorRequested;

    /// <summary>Raised (from the frame) when the action key was pressed facing a furnace: the furnace's object id (docs/adr/ADR-024).</summary>
    public event Action<int>? FurnaceRequested;

    /// <summary>Raised (from the frame) when the close key was pressed.</summary>
    public event Action? PanelCloseRequested;

    /// <summary>Gets whether the action key of the last <see cref="Update"/> opened a chest or a furnace, so nothing else (using a tool, placing a piece) may act on it.</summary>
    public bool ActionConsumed { get; private set; }

    /// <summary>Gets the tile x in front of the farmer.</summary>
    public int TargetX { get; private set; }

    /// <summary>Gets the tile y in front of the farmer.</summary>
    public int TargetY { get; private set; }

    /// <summary>
    /// Gets whether the held farm item (a seed, a hoe or a watering can) would do nothing on the tile in front: the reticle is then drawn
    /// tinted. A prediction for the reticle only; the server decides every outcome.
    /// </summary>
    public bool TargetDenied { get; private set; }

    /// <summary>Gets the item id currently held in the selected hotbar slot, or 0 when empty.</summary>
    public int HeldItemId => _inventory.SlotAt(_inventory.SelectedSlot).ItemId;

    /// <summary>Asks for a counter as if the action key had been pressed at it: a click on its merchant (docs/adr/ADR-043 D3). The caller has checked the farmer is within reach.</summary>
    /// <param name="shopId">The counter's id.</param>
    public void RequestShop(int shopId) => this.ShopRequested?.Invoke(shopId);

    /// <summary>Opens the mine elevator / way-up dialog as if the action key had been pressed next to it.</summary>
    public void RequestElevator() => this.ElevatorRequested?.Invoke();

    /// <summary>Handles the keys pressed this frame.</summary>
    /// <param name="edges">Keys pressed since the previous frame.</param>
    /// <param name="feetX">Farmer's feet x, world pixels.</param>
    /// <param name="feetY">Farmer's feet y, world pixels.</param>
    /// <param name="facing">Where the farmer looks.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <param name="mapAllowsTools">Whether tools may be used on the map being shown (not in a room where the action key places furniture); the server decides what a swing does.</param>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    /// <param name="shopId">The id of the shop whose counter is within reach, or zero; the action key then opens that shop instead of using an item.</param>
    /// <param name="placed">The pieces placed here; the action key facing a chest or a furnace opens it (the server decides whether the player may), or <see langword="null"/> where nothing is placed.</param>
    /// <param name="elevatorNear">Whether the mine elevator is within reach; the action key then opens its picker instead of using an item.</param>
    /// <param name="interior">The building the player is inside, or <see langword="null"/> outside: there the action key feeds the bench, pets an animal or collects its produce (docs/adr/ADR-028 D3).</param>
    /// <param name="walkGrid">Current walk grid including blocking nodes; used to let pickaxe swings break adjacent stones before opening the elevator.</param>
    public void Update(InputEdges edges, double feetX, double feetY, Facing facing, int tileSize, bool mapAllowsTools, double nowSeconds, int shopId = 0, PlacedSet? placed = null, bool elevatorNear = false, BuildingDefinition? interior = null, WalkGrid? walkGrid = null)
    {
        this.ActionConsumed = false;
        if (_counterId != 0 && shopId != _counterId)
        {
            this.CounterLeft?.Invoke(_counterId);
        }

        _counterId = shopId;
        int x = (int)(feetX / tileSize);
        int y = (int)(feetY / tileSize);
        _combat?.SetLocalPosition(x, y, (byte)facing);
        (this.TargetX, this.TargetY) = facing switch
        {
            Facing.Up => (x, y - 1),
            Facing.Left => (x - 1, y),
            Facing.Right => (x + 1, y),
            _ => (x, y + 1),
        };

        this.TargetDenied = IsFarmTool(this.HeldItemId) && !this.TargetWorks(mapAllowsTools);

        if (edges == InputEdges.None)
        {
            return;
        }

        int slot = InputPacking.NumberSlot(edges);
        if (slot >= 0)
        {
            _inventory.Select(slot);
        }

        this.RaisePanelKeys(edges);
        this.TryCastSkillFromEdges(edges, x, y, nowSeconds);

        if ((edges & InputEdges.Action) != 0 && shopId != 0)
        {
            this.ShopRequested?.Invoke(shopId);
            return;
        }

        SlotInfo heldForElevator = _inventory.SlotAt(_inventory.SelectedSlot);
        bool monsterInRange = CombatRules.WeaponDamage(heldForElevator.ItemId) > 0
            && _combat is not null
            && _combat.TryFindTarget(x, y, CombatRules.AttackRange(heldForElevator.ItemId), out _);
        bool pickaxeFacingObstacle = ItemCatalog.TryGet(heldForElevator.ItemId, out ItemDefinition? elevatorTool)
            && elevatorTool.Tool == ToolKind.Pickaxe
            && walkGrid is not null
            && walkGrid.IsBlocked(this.TargetX, this.TargetY);
        if ((edges & InputEdges.Action) != 0 && elevatorNear && !monsterInRange && !pickaxeFacingObstacle)
        {
            this.ElevatorRequested?.Invoke();
            return;
        }

        if ((edges & InputEdges.Action) != 0 && interior is not null)
        {
            // Inside a building the action key tends the animals and never swings a tool; the server decides what happens.
            this.ActionConsumed = true;
            if (_buildings is not null && nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
            {
                _lastUseSeconds = nowSeconds;
                this.TendAnimals(interior, feetX, feetY, tileSize);
            }

            return;
        }

        if ((edges & InputEdges.Action) != 0 && placed is not null && placed.TryFindAt(this.TargetX, this.TargetY, out PlacedItem piece)
            && PlaceableCatalog.TryGet(piece.ItemId, out PlaceableDefinition? definition) && definition.Kind is PlaceableKind.Chest or PlaceableKind.Furnace)
        {
            // The piece in front is something to use, not a tile to work or a place to set something down.
            this.ActionConsumed = true;
            if (nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
            {
                _lastUseSeconds = nowSeconds;
                if (definition.Kind == PlaceableKind.Chest)
                {
                    _chests?.Open(piece.Id);
                }
                else
                {
                    this.FurnaceRequested?.Invoke(piece.Id);
                }
            }

            return;
        }

        // The wide use: a hoe or a watering can of a higher tier works a line of tiles with one swing. The server checks the length against the tool in the slot.
        if ((edges & InputEdges.Rotate) != 0 && mapAllowsTools && nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
        {
            int wideSlot = _inventory.SelectedSlot;
            int wideItemId = _inventory.SlotAt(wideSlot).ItemId;
            if (ItemCatalog.TryGet(wideItemId, out ItemDefinition? tool) && tool.Tool is ToolKind.Hoe or ToolKind.WateringCan && ToolRules.AreaLength(tool.Tier) > 1)
            {
                _lastUseSeconds = nowSeconds;
                _swingStartSeconds = nowSeconds;
                _swingItemId = wideItemId;
                this.PlaySwing(tool.Tool);
                _farm.UseArea(wideSlot, this.TargetX, this.TargetY, ToolRules.AreaLength(tool.Tier));
            }
        }

        if ((edges & InputEdges.Action) != 0 && mapAllowsTools && nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
        {
            this.TryExecuteActionKey(x, y, nowSeconds);
        }
    }

    // Feed in hand facing the bench fills it; otherwise the animal within reach is collected from (an egg, or milk with the pail in hand) when it has produce, and petted when it has none.
    private void TendAnimals(BuildingDefinition interior, double feetX, double feetY, int tileSize)
    {
        if (_buildings is null)
        {
            return;
        }

        int selected = _inventory.SelectedSlot;
        SlotInfo held = _inventory.SlotAt(selected);
        int x = (int)(feetX / tileSize);
        int y = (int)(feetY / tileSize);
        if (AnimalRules.RationsOf(held.ItemId) > 0 && (interior.IsTrough(this.TargetX, this.TargetY) || interior.IsTrough(x, y - 1)))
        {
            _buildings.Feed(selected, interior.Capacity);
            return;
        }

        AnimalPose animal;
        using (_buildings.Read())
        {
            if (!_buildings.TryFindNear(feetX, feetY, AnimalRules.ReachPixels - 4, _buildings.NowMs, out animal))
            {
                return;
            }
        }

        bool cow = AnimalCatalog.TryGet((AnimalKind)animal.Kind, out AnimalDefinition? species) && species.NeedsPail;
        bool pail = held.ItemId == ItemCatalog.MilkPail;
        if (animal.HasProduce && (!cow || pail || animal.Petted))
        {
            // A cow that was petted already and still has milk: asking again tells the player a pail is needed.
            _buildings.Collect(animal.Id, selected);
        }
        else
        {
            _buildings.Pet(animal.Id);
        }
    }

    /// <summary>
    /// Drag-to-sow: while the mouse button is held over the world, the held seed, hoe or watering can works each tile the drag reaches, one farm
    /// use per tile. The server checks reach, seeds, energy and the tile of every use, so a refused tile changes nothing.
    /// </summary>
    /// <param name="feetX">Farmer's feet x, world pixels.</param>
    /// <param name="feetY">Farmer's feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <param name="tileX">The tile under the mouse.</param>
    /// <param name="tileY">The tile under the mouse.</param>
    /// <param name="pointerHeld">Whether the drag is on.</param>
    /// <param name="mapAllowsTools">Whether tools may be used on the map being shown.</param>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    public void DragSow(double feetX, double feetY, int tileSize, int tileX, int tileY, bool pointerHeld, bool mapAllowsTools, double nowSeconds)
    {
        if (!pointerHeld || !mapAllowsTools || !IsFarmTool(this.HeldItemId))
        {
            _dragging = false;
            return;
        }

        if ((_dragging && tileX == _dragTileX && tileY == _dragTileY) || !FarmRules.InReach(feetX, feetY, tileX, tileY, tileSize))
        {
            return;
        }

        if (_dragging && nowSeconds - _lastDragSeconds < DragStepSeconds)
        {
            return;
        }

        _dragging = true;
        _dragTileX = tileX;
        _dragTileY = tileY;
        _lastDragSeconds = nowSeconds;
        _farm.Use(_inventory.SelectedSlot, tileX, tileY);
    }

    // The items a drag works a tile with: seeds, a hoe and a watering can.
    private static bool IsFarmTool(int itemId)
        => ItemCatalog.TryGet(itemId, out ItemDefinition? item) && (item.Kind == ItemKind.Seed || (item.Kind == ItemKind.Tool && item.Tool is ToolKind.Hoe or ToolKind.WateringCan));

    // Whether the held item, or bare hands, would do something on the tile in front by the rules the server runs; the reticle is drawn from it.
    private bool TargetWorks(bool mapAllowsTools)
    {
        GameCalendar? rules = this.Rules();
        if (rules is null || !mapAllowsTools || !ItemCatalog.TryGet(this.HeldItemId, out ItemDefinition? held))
        {
            return false;
        }

        long nowMs = _farm.ServerNowMs();
        using (_farm.Read())
        {
            FarmTile? current = _farm.TryGetTile(this.TargetX, this.TargetY, out FarmTileInfo info)
                ? new FarmTile(info.CropId, info.PlantedAtMs, info.GrowthDays, info.WateredDay, info.TendedDay)
                : null;
            if (current is { HasCrop: true } && FarmRules.Harvest(current, nowMs, rules, out _, out _) == FarmVerdict.Ok)
            {
                return true;
            }

            if (held.Kind == ItemKind.Seed)
            {
                return FarmRules.Plant(current, held.CropId, nowMs, rules, out _) == FarmVerdict.Ok;
            }

            return held.Tool switch
            {
                ToolKind.Hoe => FarmRules.Till(true, current, nowMs, rules, out _) == FarmVerdict.Ok,
                ToolKind.WateringCan => !(current is { } wet && FarmRules.IsWet(wet, nowMs, rules)) && FarmRules.Water(current, nowMs, rules, out _) == FarmVerdict.Ok,
                _ => false,
            };
        }
    }

    // Whether the tile in front holds a live crop: the action key then works the crop, whatever is held.
    private bool FacesCrop()
    {
        GameCalendar? rules = this.Rules();
        long nowMs = _farm.ServerNowMs();
        using (_farm.Read())
        {
            if (!_farm.TryGetTile(this.TargetX, this.TargetY, out FarmTileInfo info) || info.CropId == 0)
            {
                return false;
            }

            if (rules is not null)
            {
                FarmTile tile = new(info.CropId, info.PlantedAtMs, info.GrowthDays, info.WateredDay, 0);
                if (FarmRules.IsWithered(tile, nowMs, rules))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private GameCalendar? Rules()
    {
        if (_calendar is not { IsLoaded: true })
        {
            return null;
        }

        if (_rules is null || _rules.EpochMs != _calendar.EpochMs || _rules.DayLengthMs != _calendar.DayLengthMs)
        {
            _rules = new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs);
        }

        return _rules;
    }

    // Whether a tile is one of the shipping bin's footprint tiles on the standard farm layout.
    private static bool IsShippingBin(int tileX, int tileY)
    {
        foreach (PropPlacement placement in FarmProps.Placements)
        {
            if (ReferenceEquals(placement.Definition, FarmProps.ShippingBin)
                && tileX >= placement.TileX && tileX < placement.TileX + placement.Definition.FootprintWidth
                && tileY >= placement.TileY && tileY < placement.TileY + placement.Definition.FootprintHeight)
            {
                return true;
            }
        }

        return false;
    }

    private void RaisePanelKeys(InputEdges edges)
    {
        if ((edges & InputEdges.Inventory) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Inventory);
        }

        if ((edges & InputEdges.Wardrobe) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Wardrobe);
        }

        if ((edges & InputEdges.Trade) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Trade);
        }

        if ((edges & InputEdges.Coop) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Coop);
        }

        if ((edges & InputEdges.Help) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Help);
        }

        if ((edges & InputEdges.Close) != 0)
        {
            this.PanelCloseRequested?.Invoke();
        }

        if ((edges & InputEdges.Profile) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Profile);
        }

        if ((edges & InputEdges.WorldMap) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.WorldMap);
        }

        if ((edges & InputEdges.Journal) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Journal);
        }
    }
}
