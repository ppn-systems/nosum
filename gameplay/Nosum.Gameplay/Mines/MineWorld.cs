// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Mines;

/// <summary>
/// Where the mine and the blacksmith stand on the placeholder maps (docs/adr/ADR-007, ADR-026): the Town edge that leads to the mine, the
/// entrance cave <c>Mine.tmx</c> and the blacksmith's counter. Tile coordinates, checked against the map files by tests.
/// </summary>
public static class MineWorld
{
    /// <summary>First Town x of the north edge that leads to the mine: the road to the Mountain, which the world does not host yet, ends here for now.</summary>
    public const int TownGateFromX = 79;

    /// <summary>Last Town x of the north edge that leads to the mine (five tiles wide, as the file's own exit to the Mountain).</summary>
    public const int TownGateToX = 83;

    /// <summary>Town tile a player arrives on when leaving the mine, one tile in from the gate (not on the edge, so they do not bounce back).</summary>
    public static readonly (int X, int Y) TownArrival = (81, 2);

    /// <summary>Tile of the entrance cave a player arrives on when coming from Town, beside the way back.</summary>
    public static readonly (int X, int Y) CaveArrival = (18, 11);

    /// <summary>Tile of the entrance cave that leads back to Town: the cave's open south passage.</summary>
    public static readonly (int X, int Y) CaveTownExit = (18, 13);

    /// <summary>Tile of the entrance cave where the shaft to the first floor is (the file's <c>Mine</c> action tile, opened so a farmer can step into it).</summary>
    public static readonly (int X, int Y) CaveShaft = (23, 9);

    /// <summary>Tile in front of the shaft: where the entrance cave's spawn is and where a player coming up from the first floor arrives.</summary>
    public static readonly (int X, int Y) CaveShaftFront = (23, 10);

    /// <summary>Tile of the mine elevator in the entrance cave (the file's <c>MineElevator</c> action tile).</summary>
    public static readonly (int X, int Y) CaveElevator = (17, 3);

    /// <summary>Tile in front of the blacksmith's door in Town (door tile (94, 81)), where the farmer stands to use the counter.</summary>
    public static readonly (int X, int Y) BlacksmithCounter = (94, 82);
}
