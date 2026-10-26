// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>The farm map under the buildings the code draws (docs/adr/ADR-042 D2): their footprints are open ground in the file.</summary>
public sealed class FarmMapTests
{
    [Fact]
    public void Placements_EveryFootprintTile_IsFreeOfBlockingTilesOnTheMapItself()
    {
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", "Farm.tmx")));
        WalkGrid ground = WalkGrid.FromMap(map);
        List<(int X, int Y)> blocked = [];

        // FarmProps.BuildWalkGrid blocks each whole footprint itself; the file must not block any of it first.
        foreach (PropPlacement prop in FarmProps.Placements)
        {
            for (int y = prop.TileY; y < prop.TileY + prop.Definition.FootprintHeight; y++)
            {
                for (int x = prop.TileX; x < prop.TileX + prop.Definition.FootprintWidth; x++)
                {
                    if (ground.IsBlocked(x, y))
                    {
                        blocked.Add((x, y));
                    }
                }
            }
        }

        blocked.Should().BeEmpty("a building is drawn over open ground");
    }
}
