// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Microsoft.Extensions.Localization;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.ViewModels.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the animals of the building the player is inside (docs/adr/ADR-028 D4) as depth-sorted world sprites, so the farmer walks in front of and behind them. The server chose each walk;
/// this only follows it, taking the position from the walk's start time every frame, and the walking frame from the clock. An animal with produce to collect shows the produce above its
/// head: the item the animal gives today, large or gold quality included (the server rolls it when collected, from the same seed). Reads the channel under its lock and draws structs; allocation-free.
/// </summary>
internal sealed class AnimalsDecorator : IMapDecorator
{
    private const int FrameMs = 160;
    private const int ProduceIcon = 16;

    // The gold ready sparkle of a ripe crop (docs/adr/ADR-021), 8 x 8, drawn over an animal that has produce today (docs/adr/ADR-090 D1).
    private const int ReadySize = 8;

    // Where a hint sits over its animal or the bench: its left edge is this far left of the anchor, and its top this far above the sprite's top.
    private const int HintInset = 24;
    private const int HintLift = 14;
    private const int HeartSize = 8;
    private const int HeartCount = 5;
    private const long HeartsShownMs = 1_500;

    // The index of the exclamation mark in the emote sheet (docs/adr/ADR-075 D1: heart, laugh, wave, question, exclamation, ...).
    private const int ExclamationEmote = 4;

    private readonly BuildingChannel _buildings;
    private readonly Dictionary<byte, (int Texture, AnimalDefinition Definition)> _sheets;
    private readonly int _objectsTexture;
    private readonly int _heartsTexture;
    private readonly int _emotesTexture;
    private readonly int _readyTexture;
    private readonly AnimalTendLabels? _tendLabels;
    private readonly PlayerActions? _actions;
    private int _seenDay;
    private readonly BuildingDefinition? _building;
    private readonly int _tileSize;
    private readonly CalendarChannel? _calendar;
    private GameCalendar? _rules;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="buildings">The animals of the room.</param>
    /// <param name="sheets">Texture and data of every species.</param>
    /// <param name="objectsTexture">Texture of the objects sheet, where produce and the hay are pictured.</param>
    /// <param name="heartsTexture">Texture of the hearts sheet: a filled heart at column 0, an empty one at column 8.</param>
    /// <param name="emotesTexture">Texture of the emote sheet: the exclamation mark that says the feeding bench is empty.</param>
    /// <param name="readyTexture">Texture of <c>ready.png</c>, the gold sparkle over an animal that has produce today.</param>
    /// <param name="tendLabels">The interned hints of the action key (docs/adr/ADR-090 D3); null draws none.</param>
    /// <param name="actions">The player's action preview; null draws none.</param>
    /// <param name="building">The building the room is, for the place of its feeding bench; null draws no bench state.</param>
    /// <param name="tileSize">Tile size of the room's map, pixels.</param>
    /// <param name="calendar">The server's calendar, for the game day the produce is rolled on; null uses the everyday produce until it is known.</param>
    public AnimalsDecorator(BuildingChannel buildings, Dictionary<byte, (int Texture, AnimalDefinition Definition)> sheets, int objectsTexture, int heartsTexture, int emotesTexture, int readyTexture, BuildingDefinition? building, int tileSize, CalendarChannel? calendar = null, AnimalTendLabels? tendLabels = null, PlayerActions? actions = null)
    {
        _buildings = buildings;
        _sheets = sheets;
        _objectsTexture = objectsTexture;
        _heartsTexture = heartsTexture;
        _emotesTexture = emotesTexture;
        _readyTexture = readyTexture;
        _tendLabels = tendLabels;
        _actions = actions;
        _building = building;
        _tileSize = tileSize;
        _calendar = calendar;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        long now = _buildings.NowMs;
        long clock = Environment.TickCount64;
        GameCalendar? rules = this.Rules();
        int today = rules?.DayNumberAt(now) ?? 0;
        if (today != 0)
        {
            // The room was read on an earlier game day: the night's feeding settles on the server only when someone asks, so ask again (docs/adr/ADR-090 D1).
            if (_seenDay != 0 && today != _seenDay)
            {
                _buildings.LoadRoom();
            }

            _seenDay = today;
        }

        using (_buildings.Read())
        {
            AnimalPetting petting = _buildings.LastPetting;
            bool showHearts = petting.AtMs > 0 && now - petting.AtMs < HeartsShownMs;
            int filledHearts = AnimalRules.HeartsOf(petting.Friendship);

            for (int i = 0; i < _buildings.AnimalCount; i++)
            {
                AnimalPose pose = _buildings.AnimalAt(i, now);
                if (!_sheets.TryGetValue(pose.Kind, out (int Texture, AnimalDefinition Definition) sheet))
                {
                    continue;
                }

                AnimalDefinition animal = sheet.Definition;

                // Rows of the sheet: down, right, up; left is the right row mirrored. Four walking frames; an animal standing still shows the first.
                int row = pose.Facing == 3 ? 1 : pose.Facing;
                int column = pose.Walking ? (int)((clock / FrameMs) % 4) : 0;
                int x = (int)Math.Round(pose.X);
                int y = (int)Math.Round(pose.Y);
                int left = x - (animal.FrameWidth / 2);
                int top = y - animal.FrameHeight + 3;
                int stableId = pose.Id.GetHashCode() & 0x1FFF;
                world.Add(sheet.Texture, column * animal.FrameWidth, row * animal.FrameHeight, animal.FrameWidth, animal.FrameHeight, left, top, y, x, stableId, pose.Facing == 3 ? RenderCommandBuffer.FlipX : 0);

                int produce = rules is null ? animal.Product : AnimalRules.ProduceOf(animal, pose.Id, today, pose.Friendship);
                if (pose.HasProduce && ItemIcons.TryGetGroundCell(produce, out int srcX, out int srcY))
                {
                    // The egg or the milk bobs above the animal, in front of everything in the room.
                    int bob = (int)(2 * Math.Sin((clock + (i * 311)) * 0.005));
                    world.Add(_objectsTexture, srcX, srcY, ProduceIcon, ProduceIcon, x - (ProduceIcon / 2), top - ProduceIcon + bob, y + 64, x);

                    // The ready sparkle marks the animal in the corner, the same mark a ripe crop wears (docs/adr/ADR-090 D1).
                    world.Add(_readyTexture, 0, 0, ReadySize, ReadySize, left + animal.FrameWidth - ReadySize, top, y + 64, x);
                }

                // The hint over the animal the action key would collect from or pet (docs/adr/ADR-090 D3).
                if (_actions is not null && _tendLabels is not null && pose.Id == _actions.TendTarget)
                {
                    int hint = _tendLabels.IdOf(_actions.TendPreview);
                    if (hint != 0)
                    {
                        world.AddOverlay(RenderCommandBuffer.LabelTexture, hint, 0, 0, 0, x - HintInset, top - HintLift, y + 65, x);
                    }
                }

                if (showHearts && pose.Id == petting.AnimalId)
                {
                    // The hearts float above the animal, above its produce when it has some, for a moment after the petting.
                    int heartsTop = top - (pose.HasProduce ? ProduceIcon : 0) - HeartSize - 2;
                    int heartsLeft = x - ((HeartCount * HeartSize) / 2);
                    for (int h = 0; h < HeartCount; h++)
                    {
                        int heartX = h < filledHearts ? 0 : HeartSize;
                        world.Add(_heartsTexture, heartX, 0, HeartSize, HeartSize, heartsLeft + (h * HeartSize), heartsTop, y + 64, x);
                    }
                }
            }

            this.AddBench(world, _buildings.Rations, _buildings.AnimalCount);
        }
    }

    // The feeding bench over the trough (docs/adr/ADR-028 D3, docs/design/critique-animals-pets.md): a hay sheaf while the bench holds feed, an exclamation mark when it is empty
    // and the animals will not eat tonight. Only the bench is shown, not its count: the trough is where the player stands to feed.
    private void AddBench(WorldSpriteBatch world, int rations, int animals)
    {
        if (_building is null || animals == 0)
        {
            return;
        }

        // The middle of the bench: a coop's and a barn's bench are four tiles wide, so the middle is the boundary between the two middle tiles.
        int troughX = (_building.TroughLeft + (_building.TroughWidth / 2)) * _tileSize;
        int troughY = _building.TroughTop * _tileSize;
        int sortY = troughY + 64;
        if (AnimalRules.BenchNeedsHay(rations, animals))
        {
            world.Add(_emotesTexture, ExclamationEmote * ProduceIcon, 0, ProduceIcon, ProduceIcon, troughX - (ProduceIcon / 2), troughY - ProduceIcon - 2, sortY, troughX);
        }
        else if (ItemIcons.TryGetGroundCell(ItemCatalog.Hay, out int srcX, out int srcY))
        {
            world.Add(_objectsTexture, srcX, srcY, ProduceIcon, ProduceIcon, troughX - (ProduceIcon / 2), troughY - ProduceIcon - 2, sortY, troughX);
        }

        // Feed in hand at the trough: the hint says the press fills the bench (docs/adr/ADR-090 D3).
        int feedHint = _tendLabels is not null && _actions is not null && _actions.TendPreview == AnimalTend.Feed ? _tendLabels.IdOf(AnimalTend.Feed) : 0;
        if (feedHint != 0)
        {
            world.AddOverlay(RenderCommandBuffer.LabelTexture, feedHint, 0, 0, 0, troughX - HintInset, troughY - ProduceIcon - HintLift, sortY, troughX);
        }
    }

    // The calendar the server sent, rebuilt only when its numbers change (once, at sign-in); null until it is known.
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
}

/// <summary>Builds the <see cref="AnimalsDecorator"/> of the inside of every farm building.</summary>
internal sealed class AnimalsDecoratorProvider : IMapDecoratorProvider
{
    private const string AnimalsUrl = "assets/sprites/animals/";
    private const string HeartsUrl = "assets/sprites/hearts.png";

    // The emote art is linked once, in Nosum.Wasm.Controls.csproj, and served under its _content path (as EmoteBubbleDecorator does).
    private const string EmotesUrl = "_content/Nosum.Wasm.Controls/ui/emotes.png";
    private const string ReadyUrl = "assets/sprites/ready.png";

    private readonly BuildingChannel _buildings;
    private readonly CalendarChannel? _calendar;
    private readonly AnimalTendLabels? _tendLabels;
    private readonly PlayerActions? _actions;

    /// <summary>Initializes the provider.</summary>
    /// <param name="buildings">The animals of the room.</param>
    /// <param name="calendar">The server's calendar, for the day's produce.</param>
    public AnimalsDecoratorProvider(BuildingChannel buildings, CalendarChannel? calendar = null, AnimalTendLabels? tendLabels = null, PlayerActions? actions = null)
    {
        _buildings = buildings;
        _calendar = calendar;
        _tendLabels = tendLabels;
        _actions = actions;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        if (!spec.IsInterior)
        {
            return null;
        }

        Dictionary<byte, (int Texture, AnimalDefinition Definition)> sheets = [];
        foreach (AnimalDefinition animal in AnimalCatalog.All)
        {
            int texture = await textures.LoadAsync(AnimalsUrl + animal.Sheet + ".png").ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            sheets[(byte)animal.Kind] = (texture, animal);
        }

        int objects = await textures.LoadAsync(ItemIcons.GroundSheetUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int hearts = await textures.LoadAsync(HeartsUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int emotes = await textures.LoadAsync(EmotesUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int ready = await textures.LoadAsync(ReadyUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        BuildingCatalog.TryGet(spec.InteriorOf, out BuildingDefinition? building);
        return new AnimalsDecorator(_buildings, sheets, objects, hearts, emotes, ready, building, map.TileWidth, _calendar, _tendLabels, _actions);
    }
}

/// <summary>
/// The three short hints of the action key inside a building (docs/adr/ADR-090 D3): what E will do, interned once from the localized strings so a frame
/// allocates nothing. The ids are those of <see cref="LabelRegistry"/>; zero means no label.
/// </summary>
internal sealed class AnimalTendLabels
{
    /// <summary>Creates the three hints.</summary>
    /// <param name="registry">The label registry the world draws from.</param>
    /// <param name="text">The localizer; null shows the keys.</param>
    public AnimalTendLabels(LabelRegistry registry, IStringLocalizer<Language>? text)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.FeedId = registry.Intern(Lookup(text, "Animal_Tend_Feed"), 0);
        this.CollectId = registry.Intern(Lookup(text, "Animal_Tend_Collect"), 0);
        this.PetId = registry.Intern(Lookup(text, "Animal_Tend_Pet"), 0);
    }

    /// <summary>Gets the label id of "Feed" (the bench).</summary>
    public int FeedId { get; }

    /// <summary>Gets the label id of "Collect" (produce in reach).</summary>
    public int CollectId { get; }

    /// <summary>Gets the label id of "Pet" (an animal in reach with no produce).</summary>
    public int PetId { get; }

    /// <summary>Returns the label id of an outcome, or zero when the action key does nothing.</summary>
    /// <param name="tend">The outcome.</param>
    public int IdOf(AnimalTend tend) => tend switch
    {
        AnimalTend.Feed => this.FeedId,
        AnimalTend.Collect => this.CollectId,
        AnimalTend.Pet => this.PetId,
        _ => 0,
    };

    private static string Lookup(IStringLocalizer<Language>? text, string key) => text is not null ? text[key].Value : key;
}
