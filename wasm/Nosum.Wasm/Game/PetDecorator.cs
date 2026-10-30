// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Calendar;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Pets;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.ViewModels.Pets;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the pet of every farmer on the map as a depth-sorted world sprite that follows its owner (docs/adr/ADR-075 D6). The owner's kind comes from the
/// look the server sent (the local player's from the pet state); the trail, the sit and the sleep are <see cref="PetFollow"/>. Every visitor sees the same
/// pet, because each client follows the same owner position stream. The pets' places are kept per owner; allocation-free once each owner was seen.
/// </summary>
internal sealed class PetDecorator : IMapDecorator
{
    private const int FrameSize = 16;
    private const double MaxStepSeconds = 0.25;

    private readonly FarmerFeed _feed;
    private readonly PetChannel _pets;
    private readonly LookChannel? _looks;
    private readonly CalendarChannel? _calendar;
    private readonly bool _inFarmhouse;
    private readonly int _catTexture;
    private readonly int _dogTexture;
    private readonly Dictionary<uint, PetTrack> _tracks = [];
    private GameCalendar? _rules;
    private double _step;
    private double _lastSeconds;
    private bool _hasLast;
    private long _serverMs;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="feed">The farmers of the frame.</param>
    /// <param name="pets">The local player's pet.</param>
    /// <param name="looks">The looks of the farmers in sight, which carry their pets; <see langword="null"/> shows only the local pet.</param>
    /// <param name="calendar">The server's calendar, for night; <see langword="null"/> keeps the pets awake.</param>
    /// <param name="inFarmhouse">Whether this map is the farmhouse, where the pets sleep at night.</param>
    /// <param name="catTexture">Texture of the cat sheet.</param>
    /// <param name="dogTexture">Texture of the dog sheet.</param>
    public PetDecorator(FarmerFeed feed, PetChannel pets, LookChannel? looks, CalendarChannel? calendar, bool inFarmhouse, int catTexture, int dogTexture)
    {
        _feed = feed;
        _pets = pets;
        _looks = looks;
        _calendar = calendar;
        _inFarmhouse = inFarmhouse;
        _catTexture = catTexture;
        _dogTexture = dogTexture;
    }

    /// <inheritdoc/>
    public void SetClock(in FrameClock clock)
    {
        _step = _hasLast ? Math.Clamp(clock.TotalSeconds - _lastSeconds, 0, MaxStepSeconds) : 0;
        _lastSeconds = clock.TotalSeconds;
        _hasLast = true;
        _serverMs = clock.ServerMs;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        bool sleeping = PetFollow.IsSleeping(_inFarmhouse, this.IsNight());
        for (int i = 0; i < _feed.Count; i++)
        {
            FarmerEntry farmer = _feed.At(i);
            int texture = this.TextureOf(farmer);
            if (texture == 0)
            {
                continue;
            }

            if (!_tracks.TryGetValue(farmer.Id, out PetTrack track))
            {
                track = PetFollow.Place(farmer.X, farmer.Y, farmer.Facing);
            }

            track = PetFollow.Step(track, farmer.X, farmer.Y, farmer.Facing, farmer.Moving, _step);
            _tracks[farmer.Id] = track;

            PetFrame frame = PetFollow.FrameOf(PetFollow.PoseOf(track, sleeping), track.Facing, track.WalkSeconds);
            int x = (int)Math.Round(track.X);
            int y = (int)Math.Round(track.Y);
            int left = x - (FrameSize / 2);
            int top = y - FrameSize + 2;
            world.Add(texture, frame.Column * FrameSize, frame.Row * FrameSize, FrameSize, FrameSize, left, top, y, x, (int)(farmer.Id & 0x1FFF), frame.FlipX ? RenderCommandBuffer.FlipX : 0);
        }
    }

    // The sheet of the farmer's pet, or zero when the farmer has none (or the sheet is not loaded).
    private int TextureOf(FarmerEntry farmer)
    {
        PetKind kind;
        if (farmer.Local)
        {
            kind = _pets.Current.Kind;
        }
        else
        {
            kind = _looks is not null && _looks.TryGet(farmer.Id, out LookInfo look) ? (PetKind)look.PetKind : PetKind.None;
        }

        return kind switch
        {
            PetKind.Cat => _catTexture,
            PetKind.Dog => _dogTexture,
            _ => 0,
        };
    }

    private bool IsNight()
    {
        if (_calendar is not { IsLoaded: true })
        {
            return false;
        }

        if (_rules is null || _rules.EpochMs != _calendar.EpochMs || _rules.DayLengthMs != _calendar.DayLengthMs)
        {
            _rules = new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs);
        }

        return PetFollow.IsNight(_rules.At(_serverMs).MinuteOfDay);
    }
}

/// <summary>Builds the <see cref="PetDecorator"/> of every map: the pets follow their owners wherever they are.</summary>
internal sealed class PetDecoratorProvider : IMapDecoratorProvider
{
    // The pet art is linked once, in Nosum.Wasm.Controls.csproj, and served under its _content path.
    private const string CatUrl = "_content/Nosum.Wasm.Controls/sprites/pets/cat.png";
    private const string DogUrl = "_content/Nosum.Wasm.Controls/sprites/pets/dog.png";

    private readonly FarmerFeed _feed;
    private readonly PetChannel _pets;
    private readonly LookChannel? _looks;
    private readonly CalendarChannel? _calendar;

    /// <summary>Initializes the provider.</summary>
    /// <param name="feed">The farmers of the frame.</param>
    /// <param name="pets">The local player's pet.</param>
    /// <param name="looks">The looks of the farmers in sight.</param>
    /// <param name="calendar">The server's calendar, for night.</param>
    public PetDecoratorProvider(FarmerFeed feed, PetChannel pets, LookChannel? looks, CalendarChannel? calendar)
    {
        _feed = feed;
        _pets = pets;
        _looks = looks;
        _calendar = calendar;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        int cat = await textures.LoadAsync(CatUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int dog = await textures.LoadAsync(DogUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new PetDecorator(_feed, _pets, _looks, _calendar, spec.Id == GameMaps.FarmHouse, cat, dog);
    }
}
