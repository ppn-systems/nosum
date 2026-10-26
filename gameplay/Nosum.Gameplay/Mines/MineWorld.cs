// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Mines;

/// <summary>
/// Where the mine and the blacksmith stand on the early maps (docs/adr/ADR-007, ADR-026, ADR-037): the Mountain's cave mouth that leads to the mine, the
/// entrance cave <c>Mine.tmx</c> and the blacksmith's counter. Tile coordinates, checked against the map files by tests.
/// </summary>
public static class MineWorld
{
    /// <summary>Tile of the Mountain a player stands on to enter the mine: the file's own <c>Mine</c> exit, the cave mouth at the north end of the path (docs/adr/ADR-037 D1).</summary>
    public static readonly (int X, int Y) MountainGate = (54, 4);

    /// <summary>Mountain tile a player arrives on when leaving the mine, one tile south of the gate (not on it, so they do not bounce back).</summary>
    public static readonly (int X, int Y) MountainArrival = (54, 5);

    /// <summary>Tile of the entrance cave a player arrives on when coming from the Mountain, beside the way back.</summary>
    public static readonly (int X, int Y) CaveArrival = (18, 11);

    /// <summary>Tile of the entrance cave that leads back to the Mountain: the cave's open south passage.</summary>
    public static readonly (int X, int Y) CaveMountainExit = (18, 13);

    /// <summary>Tile of the entrance cave where the shaft to the first floor is (the file's <c>Mine</c> action tile, opened so a farmer can step into it).</summary>
    public static readonly (int X, int Y) CaveShaft = (23, 9);

    /// <summary>Tile in front of the shaft: where the entrance cave's spawn is and where a player coming up from the first floor arrives.</summary>
    public static readonly (int X, int Y) CaveShaftFront = (23, 10);

    /// <summary>Tile of the mine elevator in the entrance cave (the file's <c>MineElevator</c> action tile).</summary>
    public static readonly (int X, int Y) CaveElevator = (17, 3);

    /// <summary>Tile in front of the blacksmith's door in Town (door tile (94, 81)), where the farmer stands to use the counter.</summary>
    public static readonly (int X, int Y) BlacksmithCounter = (94, 82);
}
