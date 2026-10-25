// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Bench.Load;

/// <summary>Where one bot reports itself to be this tick.</summary>
/// <param name="X">Feet x, world pixels.</param>
/// <param name="Y">Feet y, world pixels.</param>
/// <param name="Facing">0 down, 1 right, 2 up, 3 left.</param>
/// <param name="Moving">Whether it is walking.</param>
internal readonly record struct Position(float X, float Y, byte Facing, bool Moving);

/// <summary>
/// The thinking of a bot, with no network in it so it can be tested alone: where it stands, which exits lead to its target map, the tiles to walk
/// through to the next exit, wandering when it has nowhere to be, and which tool to use next on a farm. It moves with the server's own
/// <see cref="Walker"/>, so what it reports is what a real client would. Not thread-safe: the caller serializes the calls.
/// </summary>
internal sealed class Navigator
{
    private readonly WorldMaps _world;
    private readonly Random _random;
    private readonly string _target;
    private readonly Queue<(int X, int Y)> _waypoints = new();
    private readonly Queue<(byte Slot, int X, int Y)> _tools = new();
    private readonly HashSet<(int X, int Y)> _tilled = [];

    private Walker _walker;
    private WalkGrid _grid = null!;
    private bool _ownsGrid;
    private (float X, float Y) _reported;
    private List<MapWarp> _route = [];
    private InputState _wander;
    private double _wanderUntil;
    private bool _needPlot;
    private double _stillSince;
    private (double X, double Y) _lastProgress;
    private double _lastProgressAt;

    public Navigator(WorldMaps world, Random random, bool traveller, string target)
    {
        _world = world;
        _random = random;
        _target = target;
        this.Traveller = traveller;
    }

    /// <summary>Gets the map the bot is on.</summary>
    public MapInfo Map { get; private set; } = null!;

    /// <summary>Gets whether the bot is a traveller (it walks to <see cref="Target"/>; otherwise it farms where it started).</summary>
    public bool Traveller { get; private set; }

    /// <summary>Gets whether a traveller stands on its target map.</summary>
    public bool Arrived { get; private set; }

    /// <summary>Gets the map a traveller heads for.</summary>
    public string Target => _target;

    public double X => _walker.X;

    public double Y => _walker.Y;

    /// <summary>Puts the bot on a map: at the join, and after every exit it takes.</summary>
    /// <param name="map">The map.</param>
    /// <param name="x">Feet x.</param>
    /// <param name="y">Feet y.</param>
    /// <param name="facing">Facing, 0 down, 1 right, 2 up, 3 left.</param>
    /// <param name="now">Seconds on any monotonic clock.</param>
    public void Enter(MapInfo map, double x, double y, int facing, double now)
    {
        this.Map = map;
        _grid = map.Grid;
        _ownsGrid = false;
        _walker = new Walker(x, y, (Facing)facing);
        _waypoints.Clear();
        _tools.Clear();
        _tilled.Clear();
        _route = [];
        _needPlot = true;
        _lastProgress = (x, y);
        _lastProgressAt = now;
        this.PlanJourney();
    }

    /// <summary>
    /// The server refused a move: adopt its position, as the real client does, and plan again. A move refused as blocked teaches the bot about something
    /// the map file does not show (a tree or a rock the server placed, a placed object): the tile it tried to enter is closed on this bot's own copy of
    /// the grid, so the next path goes around it instead of into it again.
    /// </summary>
    /// <param name="x">Feet x the server keeps.</param>
    /// <param name="y">Feet y the server keeps.</param>
    /// <param name="blocked">Whether the server said the way was blocked.</param>
    /// <param name="now">Seconds on any monotonic clock.</param>
    public void Correct(double x, double y, bool blocked, double now)
    {
        if (blocked)
        {
            int ts = _grid.TileSize;
            int tx = WarpRule.TileX(_reported.X, ts);
            int ty = WarpRule.TileY(_reported.Y, ts);
            if ((tx != WarpRule.TileX(x, ts) || ty != WarpRule.TileY(y, ts)) && tx >= 0 && ty >= 0 && tx < _grid.Width && ty < _grid.Height)
            {
                if (!_ownsGrid)
                {
                    _grid = _grid.Clone();
                    _ownsGrid = true;
                }

                _grid.Block(tx, ty, 1, 1);
            }
        }

        _walker = new Walker(x, y, _walker.Facing);
        _waypoints.Clear();
        _lastProgress = (x, y);
        _lastProgressAt = now;
        this.PlanJourney();
    }

    /// <summary>Advances the bot by one report interval.</summary>
    /// <param name="seconds">Real time since the last step.</param>
    /// <param name="now">Seconds on any monotonic clock.</param>
    /// <returns>What to report to the server.</returns>
    public Position Step(double seconds, double now)
    {
        _walker.Step(this.ChooseInput(now), seconds, _grid);
        if (_walker.IsMoving)
        {
            _stillSince = now;
        }

        _reported = ((float)_walker.X, (float)_walker.Y);
        return new Position(_reported.X, _reported.Y, (byte)_walker.Facing, _walker.IsMoving);
    }

    /// <summary>The next tool use of a farmer: till, plant and water one tile, then the next one. Nothing while the bot is not on a farm or still travelling.</summary>
    /// <param name="now">Seconds on any monotonic clock.</param>
    /// <param name="tool">Hotbar slot and tile.</param>
    /// <returns>Whether there is a tool to use now.</returns>
    public bool TryNextTool(double now, out (byte Slot, int X, int Y) tool)
    {
        tool = default;
        if ((this.Traveller && !this.Arrived) || this.Map.Tillable is null)
        {
            return false;
        }

        // The server judges reach from the position it last accepted, which trails a walking player by a report or two: work standing still.
        if (_walker.IsMoving || now - _stillSince < 0.6)
        {
            return false;
        }

        if (_tools.Count == 0)
        {
            int ts = this.Map.Grid.TileSize;
            int px = WarpRule.TileX(_walker.X, ts);
            int py = WarpRule.TileY(_walker.Y, ts);
            (int X, int Y)? tile = null;
            for (int dy = -1; dy <= 1 && tile is null; dy++)
            {
                for (int dx = -1; dx <= 1 && tile is null; dx++)
                {
                    int x = px + dx;
                    int y = py + dy;
                    if (this.Map.Tillable.IsTillable(x, y) && !_tilled.Contains((x, y)) && FarmRules.InReach(_walker.X, _walker.Y, x, y, ts))
                    {
                        tile = (x, y);
                    }
                }
            }

            if (tile is not { } chosen)
            {
                // Nothing left to work within reach: walk to the nearest untouched soil.
                _needPlot = true;
                return false;
            }

            _ = _tilled.Add(chosen);
            _tools.Enqueue((0, chosen.X, chosen.Y));
            _tools.Enqueue((2, chosen.X, chosen.Y));
            _tools.Enqueue((1, chosen.X, chosen.Y));
        }

        tool = _tools.Dequeue();
        return true;
    }

    // Plans the walk to the next exit of the route, or to the next plot of soil, or marks the journey done.
    private void PlanJourney()
    {
        if (!this.Traveller)
        {
            this.PlanPlot();
            return;
        }

        if (this.Map.Id == _target)
        {
            this.Arrived = true;
            this.PlanPlot();
            return;
        }

        if (_route.Count == 0)
        {
            _route = _world.Route(this.Map.Id, _target) ?? [];
            if (_route.Count == 0)
            {
                // No way there from here: stay where it is and farm.
                this.Traveller = false;
                return;
            }
        }

        MapWarp exit = _route[0];
        if (!WarpRule.TryEdge(exit, this.Map.Grid, out int tileX, out int tileY, out _))
        {
            this.Traveller = false;
            return;
        }

        int ts = this.Map.Grid.TileSize;
        List<(int X, int Y)>? path = TilePath.Find(_grid, (WarpRule.TileX(_walker.X, ts), WarpRule.TileY(_walker.Y, ts)), (tileX, tileY));
        if (path is null)
        {
            // Walled in by something the grid does not show: wander, and plan again when it has not moved for a while.
            return;
        }

        foreach ((int x, int y) in path)
        {
            _waypoints.Enqueue((x, y));
        }
    }

    // A farmer walks to the nearest soil it has not worked yet; the tools then reach the tiles around it.
    private void PlanPlot()
    {
        if (!_needPlot || this.Map.Tillable is not { } soil || _waypoints.Count > 0)
        {
            return;
        }

        int ts = this.Map.Grid.TileSize;
        List<(int X, int Y)>? path = TilePath.FindNearest(
            _grid,
            (WarpRule.TileX(_walker.X, ts), WarpRule.TileY(_walker.Y, ts)),
            (x, y) => soil.IsTillable(x, y) && !_tilled.Contains((x, y)));
        _needPlot = false;
        if (path is null)
        {
            return;
        }

        foreach ((int x, int y) in path)
        {
            _waypoints.Enqueue((x, y));
        }
    }

    private InputState ChooseInput(double now)
    {
        this.TrackProgress(now);
        if (_needPlot && _waypoints.Count == 0 && _route.Count == 0 && (!this.Traveller || this.Arrived))
        {
            this.PlanPlot();
        }

        if (_waypoints.Count > 0)
        {
            int ts = this.Map.Grid.TileSize;
            (int tx, int ty) = _waypoints.Peek();
            double dx = ((tx + 0.5) * ts) - _walker.X;
            double dy = (((ty + 1) * ts) - (Walker.FootHeight / 2)) - _walker.Y;
            if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
            {
                _ = _waypoints.Dequeue();
                return _waypoints.Count == 0 && _route.Count > 0 ? this.OutwardInput(_route[0]) : InputState.None;
            }

            // One axis at a time keeps the foot box inside the corridor the path found.
            return Math.Abs(dx) > 2 ? (dx > 0 ? InputState.Right : InputState.Left) : (dy > 0 ? InputState.Down : InputState.Up);
        }

        // On the exit tile: keep pushing outward (an edge exit needs the player walking and facing out). Soil is worked standing still; elsewhere, stroll.
        return _route.Count > 0 ? this.OutwardInput(_route[0]) : this.Map.Tillable is not null ? InputState.None : this.Wander(now);
    }

    private InputState OutwardInput(MapWarp exit)
    {
        if (!WarpRule.TryEdge(exit, this.Map.Grid, out _, out _, out int outward))
        {
            return InputState.None;
        }

        return outward switch
        {
            0 => InputState.Down,
            1 => InputState.Right,
            2 => InputState.Up,
            3 => InputState.Left,
            _ => InputState.None,
        };
    }

    private InputState Wander(double now)
    {
        if (now >= _wanderUntil)
        {
            InputState[] choices = [InputState.None, InputState.Up, InputState.Down, InputState.Left, InputState.Right];
            _wander = choices[_random.Next(choices.Length)];
            _wanderUntil = now + 0.8 + (_random.NextDouble() * 1.4);
        }

        return _wander;
    }

    // A bot that wants to go somewhere but has not moved for three seconds is stuck behind something the grid does not show: plan again.
    private void TrackProgress(double now)
    {
        if (Math.Abs(_walker.X - _lastProgress.X) + Math.Abs(_walker.Y - _lastProgress.Y) > 2)
        {
            _lastProgress = (_walker.X, _walker.Y);
            _lastProgressAt = now;
            return;
        }

        if ((_waypoints.Count > 0 || _route.Count > 0) && now - _lastProgressAt > 3)
        {
            _lastProgressAt = now;
            _waypoints.Clear();
            this.PlanJourney();
            _wanderUntil = 0;
        }
    }
}
