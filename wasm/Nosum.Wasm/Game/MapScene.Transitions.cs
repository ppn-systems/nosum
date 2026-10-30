// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Rendering;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

internal sealed partial class MapScene
{
    // A click on a merchant standing within reach of their counter is the action key at that counter. The camera and zoom are the previous frame's, which is what the player clicked on.
    private void TalkToMerchantAt(MapSpec spec, int tileSize, int scale)
    {
        if (_actions is null)
        {
            return;
        }

        IReadOnlyList<NpcDefinition> merchants = NpcCatalog.OnMap(spec.Id);
        double worldX = _camera.Left + ((double)_pointerX / scale);
        double worldY = _camera.Top + ((double)_pointerY / scale);
        for (int i = 0; i < merchants.Count; i++)
        {
            NpcDefinition npc = merchants[i];
            double left = (npc.TileX * tileSize) - NpcSprites.ClickSlack;
            double right = ((npc.TileX + 1) * tileSize) + NpcSprites.ClickSlack;
            double bottom = (npc.TileY + 1) * tileSize;
            double top = bottom - NpcSprites.Height - NpcSprites.ClickSlack;
            if (worldX >= left && worldX < right && worldY >= top && worldY < bottom + NpcSprites.ClickSlack && ShopRules.IsAtCounter(spec, npc.CounterId, _farmer.X, _farmer.Y, tileSize))
            {
                _actions.RequestShop(npc.CounterId);
                return;
            }
        }

        int clickTileX = (int)Math.Floor(worldX / tileSize);
        int clickTileY = (int)Math.Floor(worldY / tileSize);
        if (spec.Elevator is { } lift
            && Math.Abs(clickTileX - lift.TileX) <= 1 && Math.Abs(clickTileY - lift.TileY) <= 1
            && ShopRules.InReach(new ShopPoint(0, lift.TileX, lift.TileY), _farmer.X, _farmer.Y, tileSize))
        {
            _actions.RequestElevator();
            return;
        }

        if (spec.MineFloor >= 1
            && Math.Abs(clickTileX - spec.SpawnTileX) <= 1 && Math.Abs(clickTileY - spec.SpawnTileY) <= 1
            && ShopRules.InReach(new ShopPoint(0, spec.SpawnTileX, spec.SpawnTileY), _farmer.X, _farmer.Y, tileSize))
        {
            _actions.RequestElevator();
        }
    }

    private static Walker StartWalker(MapContent content, MapStart start)
    {
        if (start.Exact)
        {
            return new Walker(start.X, start.Y, (Facing)(start.Facing & 3));
        }

        (double x, double y) = SpawnPoint.Find(content.Grid, start.TileX, start.TileY);
        return new Walker(x, y);
    }

    // Takes what the server decided: where to stand after a join or a map change, and corrections of refused moves.
    private void ApplyServerAnswers()
    {
        if (_channel is null)
        {
            return;
        }

        if (_channel.TryTakeEntry(out MapEntry entry))
        {
            // Intents already in flight when the place changes are refused too; ignore their corrections.
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
            if (_phase == Phase.Playing && string.Equals(entry.MapId, _content.Spec.Id, StringComparison.Ordinal))
            {
                // Same map (a join after a reconnect): stand where the server says.
                _farmer = new Walker(entry.X, entry.Y, (Facing)(entry.Facing & 3));
                _remote?.Reset();
            }
            else
            {
                // Another map: black out, load it in the background, swap when it is there.
                _pending = entry;
                _phase = Phase.FadingOut;
                _remote?.Reset();
                _loader.Prefetch(entry.MapId);
            }
        }

        if (_phase != Phase.FadingOut && _channel.TryTakeCorrection(out MoveCorrectionInfo correction) && correction.Sequence >= _ignoreCorrectionsBefore)
        {
            _farmer = new Walker(correction.X, correction.Y, _farmer.Facing);
        }
    }

    private void AdvanceFade(double frameSeconds)
    {
        switch (_phase)
        {
            case Phase.FadingOut:
                _fade = Math.Min(1, _fade + (frameSeconds / FadeOutSeconds));
                if (_fade >= 1)
                {
                    // Keep asking: a failed load is retried by the loader after a pause. Never wait here.
                    _loader.Prefetch(_pending.MapId);
                    if (_loader.TryGet(_pending.MapId, out MapContent? next))
                    {
                        this.SwapTo(next);
                    }
                    else
                    {
                        this.SetWaiting(_pending.MapId);
                    }
                }

                break;

            case Phase.FadingIn:
                _fade = Math.Max(0, _fade - (frameSeconds / FadeInSeconds));
                if (_fade <= 0)
                {
                    _phase = Phase.Playing;
                }

                break;

            default:
                break;
        }
    }

    // Tells the page when the black screen waits on a map to load, and which one, so it can show progress instead of a blank canvas.
    // Edge-triggered: raised once when the wait starts and once with null when it ends, never per frame.
    private void SetWaiting(string? mapId)
    {
        if (string.Equals(_waitingMapId, mapId, StringComparison.Ordinal))
        {
            return;
        }

        _waitingMapId = mapId;
        this.WaitingChanged?.Invoke(mapId);
    }

    private void SwapTo(MapContent next)
    {
        this.SetWaiting(null);
        _content = next;
        _farmer = new Walker(_pending.X, _pending.Y, (Facing)(_pending.Facing & 3));
        _walkTime = 0;
        _phase = Phase.FadingIn;
        _audio?.Play(SoundIds.Door);
        this.PrefetchNeighbours();
    }

    // Loads the maps the exits of this one lead to while the player is busy here, so the next change fades only briefly.
    private void PrefetchNeighbours()
    {
        foreach (MapWarp warp in _content.Warps)
        {
            _loader.Prefetch(warp.TargetMap);
        }
    }

    // Adds the other farmers to the same batch as the local one, buildings and props, so one
    // depth sort orders them all (docs/adr/ADR-005 D4). Allocation-free: poses are structs.
    private void AddRemoteFarmers()
    {
        if (_remote is null)
        {
            return;
        }

        double now = RemotePlayers.LocalNow();
        double animation = _remote.AnimationTime(now);
        for (int i = 0; i < _remote.Count; i++)
        {
            uint id = _remote.IdAt(i);
            if (!_remote.TryGetPose(id, now, out EntityPose pose))
            {
                continue;
            }

            bool moving = (pose.State & 4) != 0 && !pose.Held;
            int walk = moving ? 1 + (int)(animation / WalkFrameSeconds) : 0;
            LookInfo known = default;
            FarmerLook look = _looks is not null && _looks.TryGet(id, out known) ? LookSprites.Resolve(known) : this.Look;
            int rx = (int)Math.Round(pose.X);
            int ry = (int)Math.Round(pose.Y);
            FarmerSprite.Draw(_world, _farmerTextures, look, (Facing)(pose.State & 3), walk, rx, ry, (int)id, _remote.HeldItemOf(id));
            this.AddLabel(known.Nickname, rx, ry);
        }
    }

    // Remembers where a nickname goes; the labels are recorded after everything else so no sprite or roof covers them.
    // The energy a tool just spent floats above the tile it worked (docs/adr/ADR-074 D4): the combat numbers serve as the label, and reduced motion keeps it still.
    private void AddEnergyFloats()
    {
        if (_actions is null || _combatLabels is null)
        {
            return;
        }

        for (int i = 0; i < PlayerActions.FloatSlots; i++)
        {
            if (!_actions.TryGetEnergyFloat(i, _clockSeconds, out int worldX, out int worldY, out int amount, out float progress))
            {
                continue;
            }

            int labelId = _combatLabels.GetLabelId(amount, 0);
            if (labelId == 0)
            {
                continue;
            }

            int rise = ViewOptions.ReduceMotion ? 0 : (int)(progress * 14f);
            _world.AddOverlay(RenderCommandBuffer.LabelTexture, labelId, 0, 0, 0, worldX, worldY - 4 - rise, worldY + 16, worldX, 9850 + i);
        }
    }

    private void AddLabel(string? nickname, int feetX, int feetY)
    {
        if (!ViewOptions.ShowNames)
        {
            return;
        }

        int id = _labels?.IdOf(nickname) ?? 0;
        if (id > 0 && _labelCount < _labelIds.Length)
        {
            _labelIds[_labelCount] = id;
            _labelX[_labelCount] = feetX;
            _labelY[_labelCount] = feetY;
            _labelCount++;
        }
    }

    // One label command per player: the bitmap was uploaded when the name arrived, so the frame still crosses to the browser once.
    private void DrawLabels()
    {
        for (int i = 0; i < _labelCount; i++)
        {
            _commands.DrawLabel(_labelIds[i], _labelX[i] - _camera.Left, _labelY[i] - FarmerSprite.Height - _camera.Top - 1);
        }
    }
}
