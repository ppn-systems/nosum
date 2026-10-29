// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Building;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the animals of the building the player is inside (docs/adr/ADR-028 D4) as depth-sorted world sprites, so the farmer walks in front of and behind them. The server chose each walk;
/// this only follows it, taking the position from the walk's start time every frame, and the walking frame from the clock. An animal with produce to collect shows the produce above its
/// head. Reads the channel under its lock and draws structs; allocation-free.
/// </summary>
internal sealed class AnimalsDecorator : IMapDecorator
{
    private const int FrameMs = 160;
    private const int ProduceIcon = 16;
    private const int HeartSize = 8;
    private const int HeartCount = 5;
    private const long HeartsShownMs = 1_500;

    private readonly BuildingChannel _buildings;
    private readonly Dictionary<byte, (int Texture, AnimalDefinition Definition)> _sheets;
    private readonly int _objectsTexture;
    private readonly int _heartsTexture;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="buildings">The animals of the room.</param>
    /// <param name="sheets">Texture and data of every species.</param>
    /// <param name="objectsTexture">Texture of the objects sheet, where produce is pictured.</param>
    /// <param name="heartsTexture">Texture of the hearts sheet: a filled heart at column 0, an empty one at column 8.</param>
    public AnimalsDecorator(BuildingChannel buildings, Dictionary<byte, (int Texture, AnimalDefinition Definition)> sheets, int objectsTexture, int heartsTexture)
    {
        _buildings = buildings;
        _sheets = sheets;
        _objectsTexture = objectsTexture;
        _heartsTexture = heartsTexture;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        long now = _buildings.NowMs;
        long clock = Environment.TickCount64;
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

                if (pose.HasProduce && ItemIcons.TryGetGroundCell(animal.Product, out int srcX, out int srcY))
                {
                    // The egg or the milk bobs above the animal, in front of everything in the room.
                    int bob = (int)(2 * Math.Sin((clock + (i * 311)) * 0.005));
                    world.Add(_objectsTexture, srcX, srcY, ProduceIcon, ProduceIcon, x - (ProduceIcon / 2), top - ProduceIcon + bob, y + 64, x);
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
        }
    }
}

/// <summary>Builds the <see cref="AnimalsDecorator"/> of the inside of every farm building.</summary>
internal sealed class AnimalsDecoratorProvider : IMapDecoratorProvider
{
    private const string AnimalsUrl = "assets/sprites/animals/";
    private const string HeartsUrl = "assets/sprites/hearts.png";

    private readonly BuildingChannel _buildings;

    /// <summary>Initializes the provider.</summary>
    /// <param name="buildings">The animals of the room.</param>
    public AnimalsDecoratorProvider(BuildingChannel buildings) => _buildings = buildings;

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
        return new AnimalsDecorator(_buildings, sheets, objects, hearts);
    }
}
