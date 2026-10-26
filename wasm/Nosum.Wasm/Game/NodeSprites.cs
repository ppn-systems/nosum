// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Node;

namespace Nosum.Wasm.Game;

/// <summary>Texture ids of the sheets resource nodes are drawn from.</summary>
/// <param name="Oak"><c>assets/sprites/trees/oak.png</c>, the oak.</param>
/// <param name="Maple"><c>trees/maple.png</c>, the maple.</param>
/// <param name="Pine"><c>trees/pine.png</c>, the pine.</param>
/// <param name="Objects"><c>assets/sprites/items.png</c>, the object sheet (24 columns of 16 px cells).</param>
/// <param name="Mine"><c>assets/Maps/cave.png</c>, the mine's tile sheet (16 columns of 16 px cells), where the ladder is.</param>
internal readonly record struct NodeTextures(int Oak, int Maple, int Pine, int Objects, int Mine);

/// <summary>
/// The one table that says what a resource node looks like: (kind, variant, stage) to a frame of an early sheet (docs/adr/ADR-007).
/// Replacing the art means replacing this class. Every frame was checked by cutting it out of the sheet and looking at it.
/// <list type="table">
/// <listheader><term>Node</term><description>Frame</description></listheader>
/// <item><term>tree, grown</term><description>tree1/2/3_spring.png, 48 × 96 at (0, 0), drawn in two pieces (ADR-005 D4): the canopy (rows 0-63) sorts in front of the farmer standing beside the trunk, the trunk (rows 64-95) sorts by its base, so the farmer walks behind it from the north and in front of it from the south</description></item>
/// <item><term>tree, bush</term><description>16 × 32 at (0, 96)</description></item>
/// <item><term>tree, sapling</term><description>16 × 16 at (16, 128)</description></item>
/// <item><term>tree, seed</term><description>16 × 16 at (32, 128), a mound of dirt</description></item>
/// <item><term>stump</term><description>16 × 32 at (32, 96); its top 12 rows are empty</description></item>
/// <item><term>twig</term><description>object 294 or 295 (branch)</description></item>
/// <item><term>stone</term><description>object 343 (pale rocks) or 450 (brown rock)</description></item>
/// <item><term>copper ore node</term><description>object 751 (the copper mining node)</description></item>
/// <item><term>iron ore node</term><description>object 290 (the iron mining node)</description></item>
/// <item><term>ladder</term><description>cave.png cell (0, 2), tile 32 of the cave sheet: the top of a ladder going down into a dark hole</description></item>
/// <item><term>weed</term><description>object 313, 314 or 315 (green weeds)</description></item>
/// <item><term>grass</term><description>object 771 (a tuft of fiber)</description></item>
/// <item><term>forage</term><description>the object cell of the item it gives (<c>ItemDefinition.ObjectIcon</c>, docs/adr/ADR-025 D6)</description></item>
/// </list>
/// </summary>
internal static class NodeSprites
{
    private const int ObjectColumns = 24;
    private const int Cell = 16;
    private const int TreeWidth = 48;
    private const int TreeHeight = 96;
    private const int CanopyHeight = 64;

    /// <summary>How long a node shakes after a hit, milliseconds.</summary>
    public const int ShakeMs = 200;

    /// <summary>Returns the horizontal offset of a shaking node, in pixels.</summary>
    /// <param name="hitAtMs">The client clock reading of the last hit, or zero.</param>
    /// <param name="nowMs">The client clock now.</param>
    /// <returns>-1, 0 or 1; zero outside the shake.</returns>
    public static int ShakeOffset(long hitAtMs, long nowMs)
    {
        long elapsed = nowMs - hitAtMs;
        return hitAtMs == 0 || elapsed < 0 || elapsed >= ShakeMs ? 0 : (elapsed / 50) % 2 == 0 ? 1 : -1;
    }

    /// <summary>Adds a node to the depth-sorted world batch.</summary>
    /// <param name="world">The batch.</param>
    /// <param name="node">The node.</param>
    /// <param name="textures">The sheets.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    /// <param name="shakeX">Horizontal shake offset, pixels.</param>
    public static void Add(WorldSpriteBatch world, in NodeInfo node, in NodeTextures textures, int tileSize, int shakeX)
    {
        int left = (node.X * tileSize) + shakeX;
        int baseY = (node.Y + 1) * tileSize;
        int sortX = (node.X * tileSize) + (tileSize / 2);
        int id = node.Id;

        switch ((NodeKind)node.Kind)
        {
            case NodeKind.Tree:
                AddTree(world, node, textures, left, baseY, sortX, tileSize);
                break;

            case NodeKind.Stump:
                world.Add(TreeSheet(textures, node.Variant), 32, 96, Cell, 32, left, baseY - 32, baseY, sortX, id);
                break;

            case NodeKind.LargeStump:
                // Early art (docs/adr/ADR-007, ADR-037): the oak stump twice, side by side, until the art pass of M6 draws the clump.
                world.Add(TreeSheet(textures, TreeVariant.Oak), 32, 96, Cell, 32, left - (Cell / 2), baseY - 32, baseY, sortX - 1, id);
                world.Add(TreeSheet(textures, TreeVariant.Oak), 32, 96, Cell, 32, left + (Cell / 2), baseY - 32, baseY, sortX, id);
                break;

            case NodeKind.Twig:
                AddObject(world, textures, 294 + ((node.X + node.Y) & 1), left, baseY, sortX, id);
                break;

            case NodeKind.Stone:
                AddObject(world, textures, (node.X + node.Y) % 3 == 0 ? 450 : 343, left, baseY, sortX, id);
                break;

            case NodeKind.CopperOre:
                AddObject(world, textures, 751, left, baseY, sortX, id);
                break;

            case NodeKind.IronOre:
                AddObject(world, textures, 290, left, baseY, sortX, id);
                break;

            case NodeKind.Ladder:
                world.Add(textures.Mine, 0, 2 * Cell, Cell, Cell, left, baseY - Cell, baseY, sortX, id);
                break;

            case NodeKind.Weed:
                AddObject(world, textures, 313 + (((node.X * 7) + (node.Y * 13)) % 3), left, baseY, sortX, id);
                break;

            case NodeKind.Grass:
                AddObject(world, textures, 771, left, baseY, sortX, id);
                break;

            case NodeKind.Forage:
                if (ForageCatalog.TryGet(node.Variant, out ForageEntry forage) && ItemCatalog.TryGet(forage.ItemId, out ItemDefinition? item) && item.ObjectIcon > 0)
                {
                    AddObject(world, textures, item.ObjectIcon, left, baseY, sortX, id);
                }

                break;

            default:
                break;
        }
    }

    private static void AddTree(WorldSpriteBatch world, in NodeInfo node, in NodeTextures textures, int left, int baseY, int sortX, int tileSize)
    {
        int sheet = TreeSheet(textures, node.Variant);
        switch (node.Stage)
        {
            case TreeStage.Seed:
                world.Add(sheet, 32, 128, Cell, Cell, left, baseY - Cell, baseY, sortX, node.Id);
                break;

            case TreeStage.Sapling:
                world.Add(sheet, 16, 128, Cell, Cell, left, baseY - Cell, baseY, sortX, node.Id);
                break;

            case TreeStage.Bush:
                world.Add(sheet, 0, 96, Cell, 32, left, baseY - 32, baseY, sortX, node.Id);
                break;

            default:
                {
                    // The trunk stands on the tile; the canopy reaches one tile to each side and sorts after anything beside the trunk.
                    int frameLeft = left - ((TreeWidth - tileSize) / 2);
                    world.Add(sheet, 0, CanopyHeight, TreeWidth, TreeHeight - CanopyHeight, frameLeft, baseY - (TreeHeight - CanopyHeight), baseY, sortX, node.Id);
                    world.Add(sheet, 0, 0, TreeWidth, CanopyHeight, frameLeft, baseY - TreeHeight, baseY + tileSize, sortX, node.Id);
                    break;
                }
        }
    }

    private static int TreeSheet(in NodeTextures textures, byte variant) => variant switch
    {
        TreeVariant.Maple => textures.Maple,
        TreeVariant.Pine => textures.Pine,
        _ => textures.Oak,
    };

    private static void AddObject(WorldSpriteBatch world, in NodeTextures textures, int objectId, int left, int baseY, int sortX, int id)
        => world.Add(textures.Objects, objectId % ObjectColumns * Cell, objectId / ObjectColumns * Cell, Cell, Cell, left, baseY - Cell, baseY, sortX, id);
}
