// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Contracts.Placeable;
using Nosum.Core;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.ViewModels.Placement;

namespace Nosum.Wasm.Game;

/// <summary>
/// What the player does with furniture where it can be placed (docs/adr/ADR-015): the action key places the held piece (or sets down
/// a carried one), R turns it, F picks the piece in front up to move it, Q takes it back into the inventory. The same requests come
/// from the arrange panel's buttons. It only asks the server; the result shows when the server's push arrives. It also keeps the "ghost": where the
/// piece would go and whether it looks legal (the server decides). Allocation-free: the scene calls <see cref="Update"/> once per frame.
/// </summary>
internal sealed class PlacementActions
{
    private const double ActionCooldownSeconds = 0.2;

    private readonly PlaceableChannel _placed;
    private readonly InventoryChannel _inventory;
    private readonly PlacementViewModel _view;
    private readonly FarmChannel? _farm;
    private readonly NodeChannel? _nodes;
    private readonly HashSet<(int X, int Y)> _noReserved = [];
    private double _lastActionSeconds = double.NegativeInfinity;
    private BuildConfirmation _buildConfirm;
    private int _rotation;

    /// <summary>Initializes the actions.</summary>
    /// <param name="placed">Sends the requests.</param>
    /// <param name="inventory">Says what is held.</param>
    /// <param name="view">The panel: shows or hides with the map and relays its buttons.</param>
    /// <param name="farm">Optional farm mirror so tilled soil shows as invalid for placement.</param>
    /// <param name="nodes">Optional resource nodes mirror so stones/weeds/wood show as invalid for placement.</param>
    public PlacementActions(PlaceableChannel placed, InventoryChannel inventory, PlacementViewModel view, FarmChannel? farm = null, NodeChannel? nodes = null)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(view);
        _placed = placed;
        _inventory = inventory;
        _view = view;
        _farm = farm;
        _nodes = nodes;
    }

    /// <summary>Gets whether a ghost is to be drawn.</summary>
    public bool GhostVisible { get; private set; }

    /// <summary>Gets the item the ghost shows.</summary>
    public int GhostItemId { get; private set; }

    /// <summary>Gets the left tile of the ghost's footprint.</summary>
    public int GhostTileX { get; private set; }

    /// <summary>Gets the top tile of the ghost's footprint.</summary>
    public int GhostTileY { get; private set; }

    /// <summary>Gets the rotation of the ghost.</summary>
    public int GhostRotation { get; private set; }

    /// <summary>Gets whether the ghost looks placeable (the server has the last word).</summary>
    public bool GhostValid { get; private set; }

    /// <summary>Gets why the ghost cannot be placed (the same codes the server answers with), or <see cref="PlaceStatus.Ok"/>.</summary>
    public PlaceStatus GhostStatus { get; private set; }

    /// <summary>Gets the id of the piece being carried to a new spot, or zero.</summary>
    public int CarryingId { get; private set; }

    /// <summary>Handles the keys and panel requests of this frame.</summary>
    /// <param name="active">Whether the map shown is one where pieces can be placed and the farmer can act.</param>
    /// <param name="edges">Keys pressed since the previous frame.</param>
    /// <param name="targetX">Tile in front of the farmer, x.</param>
    /// <param name="targetY">Tile in front of the farmer, y.</param>
    /// <param name="facing">Where the farmer looks.</param>
    /// <param name="placedSet">The mirrored pieces as a set (for validity and for finding the piece in front).</param>
    /// <param name="mapGrid">The map's own grid, without pieces.</param>
    /// <param name="feetX">Farmer's feet x, world pixels (a piece is not placed under them).</param>
    /// <param name="feetY">Farmer's feet y, world pixels.</param>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    /// <param name="alwaysShowPanel">Whether the arrange key strip is always shown (a room) rather than only while a piece is held or carried (the farm, so tools keep the screen clean).</param>
    /// <param name="content">Optional loaded map content used to check building rules and reserved spawn/warp tiles.</param>
    public void Update(bool active, InputEdges edges, int targetX, int targetY, Facing facing, PlacedSet placedSet, WalkGrid mapGrid, double feetX, double feetY, double nowSeconds, bool alwaysShowPanel = true, MapContent? content = null)
    {
        _view.SetActive(active && (alwaysShowPanel || this.CarryingId != 0 || PlaceableCatalog.TryGet(_inventory.SlotAt(_inventory.SelectedSlot).ItemId, out _)));
        if (!active)
        {
            this.CarryingId = 0;
            _view.SetCarrying(0);
            this.GhostVisible = false;
            _view.SetGhost(false, PlaceStatus.Ok, false);
            _ = _view.TakeRequests();
            return;
        }

        PlacementRequests requests = _view.TakeRequests();
        bool rotate = (edges & InputEdges.Rotate) != 0 || (requests & PlacementRequests.Rotate) != 0;
        bool grab = (edges & InputEdges.Grab) != 0 || (requests & PlacementRequests.Grab) != 0;
        bool pickUp = (edges & InputEdges.PickUp) != 0 || (requests & PlacementRequests.PickUp) != 0;
        bool place = (edges & InputEdges.Action) != 0 || (requests & PlacementRequests.Place) != 0;

        // Grab starts carrying the piece in front (except unmovable buildings), or puts a carried piece back (cancels).
        if (grab)
        {
            if (this.CarryingId != 0)
            {
                this.CarryingId = 0;
                _view.SetCarrying(0);
            }
            else if (placedSet.TryFindAt(targetX, targetY, out PlacedItem piece)
                && PlaceableCatalog.TryGet(piece.ItemId, out PlaceableDefinition? grabDef)
                && grabDef.Kind != PlaceableKind.Building)
            {
                this.CarryingId = piece.Id;
                _rotation = piece.Rotation;
                _view.SetCarrying(piece.ItemId);
            }
        }

        // What is being positioned: the carried piece, else the held item when it can be placed.
        int slot = _inventory.SelectedSlot;
        int itemId = 0;
        if (this.CarryingId != 0 && placedSet.TryGet(this.CarryingId, out PlacedItem carried))
        {
            itemId = carried.ItemId;
        }
        else if (this.CarryingId != 0)
        {
            // Someone removed it (a push arrived): stop carrying.
            this.CarryingId = 0;
            _view.SetCarrying(0);
        }
        else
        {
            int held = _inventory.SlotAt(slot).ItemId;
            itemId = PlaceableCatalog.TryGet(held, out _) ? held : 0;
        }

        if (pickUp && placedSet.TryFindAt(targetX, targetY, out PlacedItem target) && Throttle(nowSeconds, ref _lastActionSeconds))
        {
            _placed.PickUp(target.Id);
            if (target.Id == this.CarryingId)
            {
                this.CarryingId = 0;
                _view.SetCarrying(0);
            }
        }

        if (itemId == 0 || !PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? definition))
        {
            this.GhostVisible = false;
            _view.SetGhost(false, PlaceStatus.Ok, false);
            return;
        }

        if (_rotation >= definition.Rotations)
        {
            _rotation = 0;
        }

        if (rotate)
        {
            _rotation = (_rotation + 1) % definition.Rotations;
        }

        PlaceableView view = definition.View(_rotation);
        (int tileX, int tileY) = Anchor(targetX, targetY, facing, view);
        this.GhostItemId = itemId;
        this.GhostTileX = tileX;
        this.GhostTileY = tileY;
        this.GhostRotation = _rotation;
        this.GhostStatus = this.StatusOf(definition, _rotation, tileX, tileY, view, placedSet, mapGrid, feetX, feetY, content);
        this.GhostValid = this.GhostStatus == PlaceStatus.Ok;
        this.GhostVisible = true;

        // A finished building cannot be moved or stowed (docs/adr/ADR-028), so a building that looks valid needs a second press.
        bool building = definition.Kind == PlaceableKind.Building && this.CarryingId == 0;
        if (place && Throttle(nowSeconds, ref _lastActionSeconds))
        {
            if (this.CarryingId != 0)
            {
                _placed.Move(this.CarryingId, tileX, tileY, _rotation);
                this.CarryingId = 0;
                _view.SetCarrying(0);
            }
            else if (building && this.GhostValid && !_buildConfirm.Press(tileX, tileY, nowSeconds))
            {
                // The first press only asks; the second press on the same tile builds it.
            }
            else
            {
                _placed.Place(slot, tileX, tileY, _rotation);
            }
        }

        _view.SetGhost(true, this.GhostStatus, building && _buildConfirm.IsArmed(nowSeconds));
    }

    // Why the ghost cannot go where it is, with the server's own codes so the panel can show the same sentence the server would refuse with.
    private PlaceStatus StatusOf(PlaceableDefinition definition, int rotation, int tileX, int tileY, PlaceableView view, PlacedSet placedSet, WalkGrid mapGrid, double feetX, double feetY, MapContent? content)
    {
        PlacementVerdict verdict = placedSet.Check(definition, rotation, tileX, tileY, mapGrid, _noReserved, this.CarryingId);
        if (verdict != PlacementVerdict.Ok)
        {
            return verdict switch
            {
                PlacementVerdict.BadRotation => PlaceStatus.BadRotation,
                PlacementVerdict.OutsideRoom => PlaceStatus.OutsideRoom,
                PlacementVerdict.Reserved => PlaceStatus.Reserved,
                PlacementVerdict.Overlaps => PlaceStatus.Overlaps,
                PlacementVerdict.Tilled => PlaceStatus.Tilled,
                PlacementVerdict.Node => PlaceStatus.NodeInTheWay,
                _ => PlaceStatus.Blocked,
            };
        }

        if (PlacedSet.CoversFeet(view, tileX, tileY, feetX, feetY, mapGrid.TileSize))
        {
            return PlaceStatus.PlayerInTheWay;
        }

        return this.GroundAndBuildingStatus(definition, view, tileX, tileY, placedSet, content);
    }

    private PlaceStatus GroundAndBuildingStatus(PlaceableDefinition definition, PlaceableView view, int tileX, int tileY, PlacedSet placedSet, MapContent? content)
    {
        if (definition.Kind == PlaceableKind.Building)
        {
            if (content is not null && !content.Spec.Farmable)
            {
                return PlaceStatus.NotPlaceable;
            }

            if (CountBuildings(placedSet) >= BuildingRules.MaxPerLand)
            {
                return PlaceStatus.LimitReached;
            }
        }

        if (content is not null)
        {
            for (int dy = 0; dy < view.FootprintHeight; dy++)
            {
                for (int dx = 0; dx < view.FootprintWidth; dx++)
                {
                    if (Math.Abs(tileX + dx - content.Spec.SpawnTileX) <= 1 && Math.Abs(tileY + dy - content.Spec.SpawnTileY) <= 1)
                    {
                        return PlaceStatus.Reserved;
                    }
                }
            }

            IReadOnlyList<MapWarp> warps = content.Warps;
            for (int w = 0; w < warps.Count; w++)
            {
                MapWarp warp = warps[w];
                for (int dy = 0; dy < view.FootprintHeight; dy++)
                {
                    for (int dx = 0; dx < view.FootprintWidth; dx++)
                    {
                        if (Math.Abs(tileX + dx - warp.FromX) <= 1 && Math.Abs(tileY + dy - warp.FromY) <= 1)
                        {
                            return PlaceStatus.Reserved;
                        }
                    }
                }
            }
        }

        if (_farm is not null)
        {
            using (_farm.Read())
            {
                if (content is null || string.Equals(_farm.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int dy = 0; dy < view.FootprintHeight; dy++)
                    {
                        for (int dx = 0; dx < view.FootprintWidth; dx++)
                        {
                            if (_farm.TryGetTile(tileX + dx, tileY + dy, out _))
                            {
                                return PlaceStatus.Tilled;
                            }
                        }
                    }
                }
            }
        }

        if (_nodes is not null)
        {
            using (_nodes.Read())
            {
                if (content is null || string.Equals(_nodes.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < _nodes.Count; i++)
                    {
                        NodeInfo node = _nodes.At(i);
                        if (node.X >= tileX && node.X < tileX + view.FootprintWidth
                            && node.Y >= tileY && node.Y < tileY + view.FootprintHeight)
                        {
                            return PlaceStatus.NodeInTheWay;
                        }
                    }
                }
            }
        }

        return PlaceStatus.Ok;
    }

    private static int CountBuildings(PlacedSet placedSet)
    {
        int count = 0;
        IReadOnlyList<PlacedItem> items = placedSet.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (PlaceableCatalog.TryGet(items[i].ItemId, out PlaceableDefinition? d) && d.Kind == PlaceableKind.Building)
            {
                count++;
            }
        }

        return count;
    }

    // The footprint is centred on the tile in front of the farmer sideways, and extends away from them: a wide piece in front of
    // the farmer sits centred, one to their right or left starts or ends beside them.
    private static (int X, int Y) Anchor(int targetX, int targetY, Facing facing, PlaceableView view)
    {
        int x = facing switch
        {
            Facing.Left => targetX - (view.FootprintWidth - 1),
            Facing.Right => targetX,
            _ => targetX - ((view.FootprintWidth - 1) / 2),
        };
        int y = facing switch
        {
            Facing.Up => targetY - (view.FootprintHeight - 1),
            Facing.Down => targetY,
            _ => targetY - (view.FootprintHeight - 1),
        };
        return (x, y);
    }

    private static bool Throttle(double nowSeconds, ref double last)
    {
        if (nowSeconds - last < ActionCooldownSeconds)
        {
            return false;
        }

        last = nowSeconds;
        return true;
    }
}
