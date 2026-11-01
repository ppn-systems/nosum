// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Contracts.Market;
using Nosum.Wasm.Socket.Market;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

/// <summary>
/// The pacing and feel of a use (docs/adr/ADR-074): a swing starts when the key or the mouse asks, the farmer is locked into it, the use reaches the
/// server at its hit frame, and the next action waits for the swing and a short recovery. Energy spent on a successful use floats above the tile.
/// Allocation-free: the floats live in a fixed ring; the server still decides every outcome.
/// </summary>
internal sealed partial class PlayerActions
{
    // Energy "-N" labels on screen at once; the ring drops the oldest.
    internal const int FloatSlots = 4;
    private const double EnergyFloatSeconds = 0.9;

    // Chests, furnaces and animals are not swung; the next action waits this long after opening or tending one.
    private const double OpenCooldownSeconds = ToolPacing.MinIntervalMs / 1000.0;
    private const double RecoverySeconds = ToolPacing.RecoveryMs / 1000.0;

    private readonly EnergyFloat[] _energyFloats = new EnergyFloat[FloatSlots];
    private double _nowSeconds;
    private double _nextUseSeconds = double.NegativeInfinity;
    private double _swingStartSeconds = double.NegativeInfinity;
    private double _swingSeconds;
    private int _swingItemId;
    private bool _useQueued;
    private double _useAtSeconds;
    private int _queuedSlot;
    private int _queuedTileX;
    private int _queuedTileY;
    private int _queuedTiles;
    private ToolKind _queuedTool;
    private int _queuedCost;
    private int _queuedFloatX;
    private int _queuedFloatY;
    private int _firedCost;

    // Whether the queued use came from the action key with an item in hand (not the wide key or a drag), the item, and the tiles: the action key's
    // harvest of a ripe crop with another item in hand is named on the message line (ActionKeyHarvested).
    private bool _queuedByActionKey;
    private int _queuedItemId;
    private bool _firedByActionKey;
    private int _firedItemId;
    private int _firedTiles;
    private int _firedFloatX;
    private int _firedFloatY;
    private int _nextFloat;
    private Facing? _turnTo;
    private bool _outdoors = true;

    /// <summary>Gets whether the farmer is exhausted: low energy makes every swing slower and shows a sweat bubble (docs/adr/ADR-074 D4).</summary>
    public bool Exhausted => _progress is { MaxEnergy: > 0 } progress && ToolPacing.IsExhausted(progress.Energy, progress.MaxEnergy);

    /// <summary>Gets whether a swing holds the farmer in place at <paramref name="nowSeconds"/>: no walking until the swing is over.</summary>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    public bool SwingLocked(double nowSeconds) => nowSeconds < _swingStartSeconds + _swingSeconds;

    /// <summary>Tells the actions whether the map is outdoors, where rain waters the soil and the crops (docs/adr/ADR-027 D2).</summary>
    /// <param name="outdoors">Whether the map is outdoors.</param>
    public void SetOutdoors(bool outdoors) => _outdoors = outdoors;

    /// <summary>
    /// Takes the turn a drag asked for (docs/adr/ADR-074 D7): the farmer turns to the tile it works before the swing shows, so the body faces the
    /// side the swing reaches. The scene applies it once, in the same frame.
    /// </summary>
    /// <param name="facing">The facing to turn to.</param>
    /// <returns><see langword="true"/> when a turn was waiting.</returns>
    public bool TryTakeTurn(out Facing facing)
    {
        if (_turnTo is { } turn)
        {
            _turnTo = null;
            facing = turn;
            return true;
        }

        facing = Facing.Down;
        return false;
    }

    /// <summary>Drops a use that was queued for the hit frame, for example when the farmer leaves the map before it lands.</summary>
    public void CancelQueuedUse() => _useQueued = false;

    /// <summary>
    /// Gets an energy label that is still rising above a tile, if one is on screen.
    /// </summary>
    /// <param name="index">Ring slot, from 0 to <see cref="FloatSlots"/> minus one.</param>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    /// <param name="worldX">Tile centre x, world pixels.</param>
    /// <param name="worldY">Tile centre y, world pixels.</param>
    /// <param name="amount">Energy spent.</param>
    /// <param name="progress">Normalized age in <c>[0, 1)</c>.</param>
    /// <returns><see langword="true"/> while the label is shown.</returns>
    public bool TryGetEnergyFloat(int index, double nowSeconds, out int worldX, out int worldY, out int amount, out float progress)
    {
        EnergyFloat floated = _energyFloats[index];
        double elapsed = nowSeconds - floated.StartSeconds;
        if (floated.Amount > 0 && elapsed >= 0.0 && elapsed < EnergyFloatSeconds)
        {
            worldX = floated.X;
            worldY = floated.Y;
            amount = floated.Amount;
            progress = (float)(elapsed / EnergyFloatSeconds);
            return true;
        }

        worldX = 0;
        worldY = 0;
        amount = 0;
        progress = 0f;
        return false;
    }

    // Starts a swing of a tool (or of bare hands and seeds): its length depends on the tool and on exhaustion, and the next action waits for it and the recovery.
    private void BeginSwing(ToolKind tool, int itemId, double nowSeconds)
    {
        _swingStartSeconds = nowSeconds;
        _swingSeconds = ToolPacing.SwingMs(tool, this.Exhausted) / 1000.0;
        _swingItemId = itemId;
        _nextUseSeconds = nowSeconds + _swingSeconds + RecoverySeconds;
    }

    // Queues a use of the held item for the hit frame of the swing just begun; the tiles of a wide use are one request.
    private void QueueUse(int slot, int tileX, int tileY, int tiles, ToolKind tool, int tileSize, bool byActionKey = false)
    {
        _useQueued = true;
        _queuedByActionKey = byActionKey;
        _queuedItemId = _swingItemId;
        _useAtSeconds = _swingStartSeconds + (_swingSeconds * ToolPacing.HitFraction);
        _queuedSlot = slot;
        _queuedTileX = tileX;
        _queuedTileY = tileY;
        _queuedTiles = tiles;
        _queuedTool = tool;
        // Every tile of a wide use is charged its own energy when it works (docs/adr/ADR-088); the label shows the total once the server says how many worked.
        _queuedCost = EnergyCostOf(tool);
        _queuedFloatX = (tileX * tileSize) + (tileSize / 2);
        _queuedFloatY = (tileY * tileSize) + (tileSize / 2);
        this.PressJuice(slot, tileX, tileY, tool, tileSize);
    }

    // The flecks of the action start when it is pressed, so they show within a frame; they are predicted from the held item and the tile (a harvest
    // only when the crop there is ripe). The server's answer still decides the outcome.
    private void PressJuice(int slot, int tileX, int tileY, ToolKind tool, int tileSize)
    {
        long nowMs = (long)(_swingStartSeconds * 1000);
        if (tool == ToolKind.Hoe)
        {
            this.Juice.Press(FarmJuiceKind.Till, tileX, tileY, tileSize, nowMs);
        }
        else if (tool == ToolKind.WateringCan)
        {
            this.Juice.Press(FarmJuiceKind.Water, tileX, tileY, tileSize, nowMs);
        }
        else if (tool == ToolKind.Scythe && this.RipeAt(tileX, tileY))
        {
            this.Juice.Press(FarmJuiceKind.Harvest, tileX, tileY, tileSize, nowMs);
        }
        else if (ItemCatalog.TryGet(_inventory.SlotAt(slot).ItemId, out ItemDefinition? held) && held.Kind == ItemKind.Seed)
        {
            this.Juice.Press(FarmJuiceKind.Plant, tileX, tileY, tileSize, nowMs);
        }
    }

    // Whether the tile holds a crop that is ripe now, by the same rule as the farm draws it (FarmRules.IsMature). Sprinklers are not counted here,
    // which can only hide a pop, never show one that is not due.
    private bool RipeAt(int tileX, int tileY)
    {
        GameCalendar? rules = this.Rules();
        if (rules is null)
        {
            return false;
        }

        using (_farm.Read())
        {
            if (!_farm.TryGetTile(tileX, tileY, out FarmTileInfo info) || info.CropId == 0 || info.Withered)
            {
                return false;
            }

            return FarmRules.IsMature(new FarmTile(info.CropId, info.PlantedAtMs, info.GrowthDays, info.WateredDay, info.TendedDay), _farm.ServerNowMs(), rules, _outdoors);
        }
    }

    // The bin's answer: a sale that paid makes the coins pop over the bin the farmer shipped to (the server decides the price and the payment).
    private void OnMarketCompleted(MarketAction action, MarketStatus status, long earned)
    {
        if (action == MarketAction.Ship && status == MarketStatus.Ok && earned > 0)
        {
            this.Juice.Press(FarmJuiceKind.Coins, _shipTileX, _shipTileY, _tileSizePx, (long)(_nowSeconds * 1000));
        }
    }

    // At the hit frame the use goes to the server and the hit sounds; the energy label waits for the server's answer.
    private void FireDueUse(double nowSeconds)
    {
        if (!_useQueued || nowSeconds < _useAtSeconds)
        {
            return;
        }

        _useQueued = false;
        _audio?.Play(AudioCatalog.ToolSound(_queuedTool));
        _firedCost = _queuedCost;
        _firedByActionKey = _queuedByActionKey;
        _firedItemId = _queuedItemId;
        _firedTiles = _queuedTiles;
        _firedFloatX = _queuedFloatX;
        _firedFloatY = _queuedFloatY;
        if (_queuedTiles > 1)
        {
            _farm.UseArea(_queuedSlot, _queuedTileX, _queuedTileY, _queuedTiles);
        }
        else
        {
            _farm.Use(_queuedSlot, _queuedTileX, _queuedTileY);
        }
    }

    // The server's answer: energy is spent only on an outcome that worked the tile, so the label floats only then.
    private void OnFarmUseCompleted(FarmStatus status, FarmAction action, int worked, int daysLeft)
    {
        _ = daysLeft;

        // The action key harvested a ripe crop while another item was in hand: the player is told the key did it (not the item in hand).
        bool keyHarvest = status == FarmStatus.Ok && action == FarmAction.Harvested && _firedByActionKey && _firedTiles == 1 && _firedItemId is not (0 or ItemCatalog.Scythe);
        _firedByActionKey = false;
        if (keyHarvest)
        {
            this.ActionKeyHarvested?.Invoke();
        }

        // The server charged the cost of each tile that worked, so the label is the cost of one tile times the count (docs/adr/ADR-088).
        int cost = _firedCost * worked;
        _firedCost = 0;
        if (status != FarmStatus.Ok || cost <= 0 || !ChargesEnergy(action))
        {
            return;
        }

        _energyFloats[_nextFloat] = new EnergyFloat(_firedFloatX, _firedFloatY, cost, _nowSeconds);
        _nextFloat = (_nextFloat + 1) % FloatSlots;
    }

    private static bool ChargesEnergy(FarmAction action)
        => action is FarmAction.Tilled or FarmAction.Watered or FarmAction.Hit or FarmAction.Broke or FarmAction.Cleared or FarmAction.CutWithered;

    // The cost the server will charge at the level the player has in the tool's skill and the tier of the tool in hand (docs/adr/ADR-023, ADR-089);
    // the server is the one that charges it.
    private int EnergyCostOf(ToolKind tool) => tool == ToolKind.None ? 0 : EnergyRules.Cost(tool, this.SkillLevelOf(tool), ToolRules.TierOf(_swingItemId));

    private int SkillLevelOf(ToolKind tool)
    {
        if (_progress is null)
        {
            return 0;
        }

        return SkillRules.SkillOf(tool) switch
        {
            SkillKind.Farming => SkillRules.LevelOf(_progress.FarmingXp),
            SkillKind.Foraging => SkillRules.LevelOf(_progress.ForagingXp),
            SkillKind.Mining => SkillRules.LevelOf(_progress.MiningXp),
            _ => 0,
        };
    }

    private ToolKind HeldTool()
        => ItemCatalog.TryGet(this.HeldItemId, out ItemDefinition? held) ? held.Tool : ToolKind.None;

    private readonly record struct EnergyFloat(int X, int Y, int Amount, double StartSeconds);
}
