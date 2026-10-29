// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

/// <summary>
/// The pacing and feel of a use (docs/adr/ADR-072): a swing starts when the key or the mouse asks, the farmer is locked into it, the use reaches the
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
    private int _firedFloatX;
    private int _firedFloatY;
    private int _nextFloat;

    /// <summary>Gets whether the farmer is exhausted: low energy makes every swing slower and shows a sweat bubble (docs/adr/ADR-072 D4).</summary>
    public bool Exhausted => _progress is { MaxEnergy: > 0 } progress && ToolPacing.IsExhausted(progress.Energy, progress.MaxEnergy);

    /// <summary>Gets whether a swing holds the farmer in place at <paramref name="nowSeconds"/>: no walking until the swing is over.</summary>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    public bool SwingLocked(double nowSeconds) => nowSeconds < _swingStartSeconds + _swingSeconds;

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
    private void QueueUse(int slot, int tileX, int tileY, int tiles, ToolKind tool, int tileSize)
    {
        _useQueued = true;
        _useAtSeconds = _swingStartSeconds + (_swingSeconds * ToolPacing.HitFraction);
        _queuedSlot = slot;
        _queuedTileX = tileX;
        _queuedTileY = tileY;
        _queuedTiles = tiles;
        _queuedTool = tool;
        _queuedCost = tiles == 1 ? EnergyCostOf(tool) : 0;
        _queuedFloatX = (tileX * tileSize) + (tileSize / 2);
        _queuedFloatY = (tileY * tileSize) + (tileSize / 2);
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
    private void OnFarmUseCompleted(FarmStatus status, FarmAction action)
    {
        int cost = _firedCost;
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

    // The cost the server will charge at the level the player has in the tool's skill (docs/adr/ADR-023); the server is the one that charges it.
    private int EnergyCostOf(ToolKind tool) => tool == ToolKind.None ? 0 : EnergyRules.Cost(tool, this.SkillLevelOf(tool));

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
