// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Economy;
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
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

/// <summary>
/// What the player's one-shot keys do: number keys select a hotbar slot, the action key (E or Space) uses the
/// held item on the tile in front of the farmer (or ships it when that tile is the shipping bin, or eats it when it is food and the player has room for energy), I M C T G H N open the panels, Escape closes them; next to a shop the action key opens it, next to the mine elevator it opens the elevator picker.
/// The rotate key (R) is the wide use of an upgraded hoe or watering can: one swing over a line of three or five tiles (docs/adr/ADR-026 D1). The scene calls <see cref="Update"/> once per
/// frame; the tile in front is kept in <see cref="TargetX"/> and <see cref="TargetY"/> for the cursor the farm decorator draws.
/// Allocation-free; the requests themselves are sent by the channels, off the frame.
/// </summary>
internal sealed class PlayerActions
{
    // A cooldown just over the server's 300 ms swing keeps a mashed key from being refused (docs/adr/ADR-021 D8) and inside its rate limit.
    private const double UseCooldownSeconds = 0.32;
    private const double SwingDurationSeconds = 0.30;

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

    /// <summary>Gets the item id currently held in the selected hotbar slot, or 0 when empty.</summary>
    public int HeldItemId => _inventory.SlotAt(_inventory.SelectedSlot).ItemId;

    /// <summary>Gets the active tool/weapon swing animation progress in <c>[0, 1)</c>, if a swing is playing.</summary>
    /// <param name="nowSeconds">Current scene clock in seconds.</param>
    /// <param name="progress">Normalized progress from 0 (wind-up) to 1 (complete).</param>
    /// <param name="itemId">Item id being swung.</param>
    /// <returns><see langword="true"/> while a swing animation is active.</returns>
    public bool TryGetActiveSwing(double nowSeconds, out float progress, out int itemId)
    {
        double elapsed = nowSeconds - _swingStartSeconds;
        if (elapsed >= 0.0 && elapsed < SwingDurationSeconds)
        {
            progress = (float)(elapsed / SwingDurationSeconds);
            itemId = _swingItemId;
            return true;
        }

        progress = -1f;
        itemId = 0;
        return false;
    }

    /// <summary>Gets the active combat skill visual effect, if any skill was recently triggered.</summary>
    /// <param name="nowMs">Current tick count in milliseconds.</param>
    /// <param name="skillId">Skill id (<c>1..3</c>).</param>
    /// <param name="progress">Normalized animation progress in <c>[0, 1)</c>.</param>
    /// <param name="playerX">Farmer tile X when cast.</param>
    /// <param name="playerY">Farmer tile Y when cast.</param>
    /// <returns><see langword="true"/> while a skill effect animation is active.</returns>
    public bool TryGetActiveSkillEffect(long nowMs, out byte skillId, out float progress, out int playerX, out int playerY)
    {
        if (_combat is not null)
        {
            return _combat.TryGetActiveSkillEffect(nowMs, out skillId, out progress, out playerX, out playerY);
        }

        skillId = 0;
        progress = 0f;
        playerX = 0;
        playerY = 0;
        return false;
    }

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

        if (_combat is not null)
        {
            if ((edges & InputEdges.Skill1) != 0 && this.CanCastSkill(1))
            {
                _swingStartSeconds = nowSeconds;
                _swingItemId = this.HeldItemId;
                _combat.CastSkill(1);
            }
            else if ((edges & InputEdges.Skill2) != 0 && this.CanCastSkill(2))
            {
                int targetId = 0;
                if (_combat.TryFindTarget(x, y, 6, out MonsterClientRow tm))
                {
                    targetId = tm.MonsterId;
                }

                _swingStartSeconds = nowSeconds;
                _swingItemId = this.HeldItemId;
                _combat.CastSkill(2, targetId);
            }
            else if ((edges & InputEdges.Skill3) != 0 && this.CanCastSkill(3))
            {
                _swingStartSeconds = nowSeconds;
                _swingItemId = this.HeldItemId;
                _combat.CastSkill(3);
            }
        }

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
            _lastUseSeconds = nowSeconds;
            int selected = _inventory.SelectedSlot;
            SlotInfo held = _inventory.SlotAt(selected);
            if (PlaceableCatalog.TryGet(held.ItemId, out _))
            {
                // A placeable in hand is set down by the placement actions, never swung at a tile.
                return;
            }

            if (IsShippingBin(this.TargetX, this.TargetY) && ShippingPrices.TryGet(held.ItemId, out _))
            {
                // Facing the bin with a crop in hand: sell the stack (the server checks the distance and prices it).
                _market.Ship(selected, held.ItemId, 0);
            }
            else if (CombatRules.WeaponDamage(held.ItemId) > 0)
            {
                _swingStartSeconds = nowSeconds;
                _swingItemId = held.ItemId;
                if (ItemCatalog.TryGet(held.ItemId, out ItemDefinition? weaponDef))
                {
                    this.PlaySwing(weaponDef.Tool);
                }

                if (_combat is not null && _combat.TryFindTarget(x, y, CombatRules.AttackRange(held.ItemId), out MonsterClientRow targetMonster))
                {
                    if (held.ItemId == ItemCatalog.ArcaneStaff)
                    {
                        _combat.SpawnProjectile(x, y, targetMonster.X, targetMonster.Y, 1);
                    }

                    _combat.Attack(targetMonster.MonsterId, selected);
                }
            }
            else if (this.CanUsePotion(held.ItemId) && !this.FacesCrop())
            {
                _audio?.Play(SoundIds.Pickup);
                _combat?.DrinkPotion(selected);
            }
            else if (_progress is { MaxEnergy: > 0 } && _progress.Energy < _progress.MaxEnergy && ItemCatalog.TryGet(held.ItemId, out ItemDefinition? food) && food.IsEdible
                && !this.FacesCrop())
            {
                // Food in hand, room for energy and no crop in front (facing a crop the action harvests it): eat it (the server removes one and
                // restores its energy, docs/adr/ADR-023 D2).
                _progress.Eat(selected);
            }
            else
            {
                if (ItemCatalog.TryGet(held.ItemId, out ItemDefinition? swung))
                {
                    if (swung.Tool != ToolKind.None)
                    {
                        _swingStartSeconds = nowSeconds;
                        _swingItemId = held.ItemId;
                    }

                    this.PlaySwing(swung.Tool);
                }

                _farm.Use(selected, this.TargetX, this.TargetY);
            }
        }
    }

    private bool CanCastSkill(byte skillId)
    {
        if (_combat is null || !SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition skill))
        {
            return false;
        }

        if (_combat.GetSkillCooldownRemaining(skillId) > 0f || _combat.Mp < skill.MpCost)
        {
            return false;
        }

        return skillId != (byte)CombatSkillKind.DivineHeal || _combat.Hp < _combat.MaxHp;
    }

    private bool CanUsePotion(int itemId)
    {
        if (_combat is null || !CombatRules.IsPotion(itemId))
        {
            return false;
        }

        if (itemId is ItemCatalog.ArcaneGrimoire or ItemCatalog.HeartCrystal)
        {
            return true;
        }

        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(itemId);
        bool healsHp = hpRestore > 0 && _combat.Hp < _combat.MaxHp;
        bool restoresMp = mpRestore > 0 && _combat.Mp < _combat.MaxMp;
        return healsHp || restoresMp;
    }

    // The swing sounds when the key is pressed, not when the server answers: the sound is feedback for the key, the server still decides what the swing did.
    private void PlaySwing(ToolKind tool) => _audio?.Play(AudioCatalog.ToolSound(tool));

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

        bool cow = animal.Kind == (byte)AnimalKind.Cow;
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
    }
}
