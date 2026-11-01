// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>What a farming action looks like on the tile it works (backlog item 9).</summary>
internal enum FarmJuiceKind
{
    /// <summary>Tilling: brown soil flecks.</summary>
    Till,

    /// <summary>Planting: pale sprout flecks.</summary>
    Plant,

    /// <summary>Watering: blue drops.</summary>
    Water,

    /// <summary>Harvesting: gold flecks and a small pop over the crop.</summary>
    Harvest,

    /// <summary>A sale that paid at the shipping bin: gold coin flecks and a coin pop over the bin.</summary>
    Coins,
}

/// <summary>
/// The farming juice pass (docs/design/research-hud-and-loops.md, item 9): short flecks and pops over the tile an action works. The flecks start when
/// the action is pressed (the server answers later and never delays them), and each lives at most 200 ms. A sale's coin pop starts when the bin's answer
/// pays. The sound of each action is already in place (docs/adr/ADR-039 D4), so this only draws. Every style is a constant and the flecks come from one
/// fixed pool, so nothing is allocated per frame, and the flecks are drawn as world sprites in the frame's one submit (docs/adr/ADR-002).
/// With reduced motion (<see cref="ViewOptions.ReduceMotion"/>) the flecks and pops stay where they start and only fade.
/// </summary>
internal sealed class FarmJuice
{
    private static readonly FleckStyle s_soil = new(Rgb: 0x7A4E2D, StartSize: 3, EndSize: 2, LifeMs: 180, TravelPx: 8, LiftPx: 0);
    private static readonly FleckStyle s_sprout = new(Rgb: 0x7BC55A, StartSize: 3, EndSize: 2, LifeMs: 180, TravelPx: 6, LiftPx: 6);
    private static readonly FleckStyle s_water = new(Rgb: 0x7FD4FF, StartSize: 2, EndSize: 2, LifeMs: 200, TravelPx: 6, LiftPx: 0);
    private static readonly FleckStyle s_harvest = new(Rgb: 0xFFD84D, StartSize: 3, EndSize: 2, LifeMs: 200, TravelPx: 10, LiftPx: 8);
    private static readonly FleckStyle s_harvestPop = new(Rgb: 0xFFF3A6, StartSize: 4, EndSize: 12, LifeMs: 200, TravelPx: 0, LiftPx: 4);
    private static readonly FleckStyle s_coin = new(Rgb: 0xF5B800, StartSize: 3, EndSize: 2, LifeMs: 200, TravelPx: 12, LiftPx: 14);
    private static readonly FleckStyle s_coinPop = new(Rgb: 0xFFD21F, StartSize: 5, EndSize: 10, LifeMs: 200, TravelPx: 0, LiftPx: 10);

    private const int BurstFlecks = 6;
    private const int SproutFlecks = 4;

    private readonly FleckBurst _flecks = new();
    private int _seed;

    /// <summary>Starts the flecks of an action on a tile, at the moment it is pressed.</summary>
    /// <param name="kind">What the action does.</param>
    /// <param name="tileX">The tile it works, in tile units.</param>
    /// <param name="tileY">The tile it works, in tile units.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    public void Press(FarmJuiceKind kind, int tileX, int tileY, int tileSize, long nowMs)
    {
        if (tileSize <= 0)
        {
            return;
        }

        bool reduced = ViewOptions.ReduceMotion;
        int x = (tileX * tileSize) + (tileSize / 2);
        int sortY = (tileY * tileSize) + (tileSize / 2) + 1;
        int y = sortY - 1;
        _seed++;
        switch (kind)
        {
            case FarmJuiceKind.Till:
                _flecks.Spawn(x, y, sortY, BurstFlecks, s_soil, nowMs, reduced, _seed);
                break;
            case FarmJuiceKind.Plant:
                _flecks.Spawn(x, y, sortY, SproutFlecks, s_sprout, nowMs, reduced, _seed);
                break;
            case FarmJuiceKind.Water:
                _flecks.Spawn(x, y, sortY, BurstFlecks, s_water, nowMs, reduced, _seed);
                break;
            case FarmJuiceKind.Harvest:
                _flecks.Spawn(x, y, sortY, BurstFlecks, s_harvest, nowMs, reduced, _seed);
                _flecks.Spawn(x, y, sortY, 1, s_harvestPop, nowMs, reduced, _seed);
                break;
            case FarmJuiceKind.Coins:
                _flecks.Spawn(x, y, sortY, BurstFlecks, s_coin, nowMs, reduced, _seed);
                _flecks.Spawn(x, y, sortY, 1, s_coinPop, nowMs, reduced, _seed);
                break;
        }
    }

    /// <summary>Adds this frame's live flecks to the world batch; the batch sorts them with the crops and the farmer.</summary>
    /// <param name="world">Where the flecks are drawn.</param>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    public void AddTo(WorldSpriteBatch world, long nowMs) => _flecks.AddTo(world, nowMs);
}
