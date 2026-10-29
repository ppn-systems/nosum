// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Ambient;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;

namespace Nosum.Wasm.Game;

/// <summary>
/// The wildlife of an outdoor map (docs/adr/ADR-071): birds that hop and fly off when the farmer comes near, frogs that jump aside, squirrels in the Woods,
/// butterflies by day in spring and summer and fireflies on summer nights. Each chunk in view is planned by <see cref="CritterPlanner"/> for today, so
/// every player sees about the same scene; culled to the camera. Motion is a function of the clock and the farmer's distance, so nothing is stored per
/// critter. With reduced motion nothing moves or flees. Frame path: no allocation, no await.
/// </summary>
internal sealed class CritterDecorator : IMapDecorator
{
    // Frames on the ambient sheet (16 x 16 cells, see tools/art_repack/ambient.py).
    private const int Size = 16;
    private const int CellPerch = 0;
    private const int CellFly = 16;
    private const int CellFrogSit = 32;
    private const int CellFrogJump = 48;
    private const int CellLit = 64;
    private const int CellDim = 80;
    private const int RowAnimal = 0;
    private const int RowGround = 16;
    private const int CellStand = 0;
    private const int CellRun = 16;
    private const int CellOpen = 32;
    private const int CellClosed = 48;

    // How near the farmer must come before a critter moves away, in pixels.
    private const double BirdFlee = 40;
    private const double SquirrelFlee = 36;
    private const double FrogFlee = 22;

    // A critter moves at most this far from its spot, so culling keeps this margin around it.
    private const int Reach = 24;
    private const int HopMs = 160;
    private const int BlinkMs = 700;
    private const int WingMs = 140;

    private readonly int _texture;
    private readonly CalendarChannel _calendar;
    private readonly int _mapSalt;
    private readonly bool _woods;
    private readonly ICritterTerrain _terrain;
    private readonly int _tileSize;
    private readonly CritterSpawn[] _spawns = new CritterSpawn[CritterPlanner.MaxPerChunk];
    private Camera2D _camera;
    private double _farmerX;
    private double _farmerY;
    private long _clockMs;
    private GameCalendar? _rules;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="texture">The ambient sheet.</param>
    /// <param name="calendar">The server's calendar: the day, the season and the time of day.</param>
    /// <param name="mapSalt">A value derived from the map's id.</param>
    /// <param name="woods">Whether the map is the Woods.</param>
    /// <param name="terrain">The ground of the map.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public CritterDecorator(int texture, CalendarChannel calendar, int mapSalt, bool woods, ICritterTerrain terrain, int tileSize)
    {
        _texture = texture;
        _calendar = calendar;
        _mapSalt = mapSalt;
        _woods = woods;
        _terrain = terrain;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void SetView(in Camera2D camera) => _camera = camera;

    /// <inheritdoc/>
    public void SetClock(in FrameClock clock) => _clockMs = clock.LocalMs;

    /// <inheritdoc/>
    public void SetFarmer(double x, double y)
    {
        _farmerX = x;
        _farmerY = y;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        GameCalendar? rules = this.Rules();
        if (rules is null)
        {
            return;
        }

        long now = _calendar.ServerNowMs();
        GameDate date = rules.At(now);
        int day = rules.DayNumberAt(now);
        int chunkPx = ChunkCoord.SizeTiles * _tileSize;
        int firstX = FloorDiv(_camera.Left, chunkPx) - 1;
        int lastX = FloorDiv(_camera.Left + _camera.Width, chunkPx) + 1;
        int firstY = FloorDiv(_camera.Top, chunkPx) - 1;
        int lastY = FloorDiv(_camera.Top + _camera.Height, chunkPx) + 1;
        bool reduce = ViewOptions.ReduceMotion;
        for (int cy = firstY; cy <= lastY; cy++)
        {
            for (int cx = firstX; cx <= lastX; cx++)
            {
                int count = CritterPlanner.Plan(CritterSeed, _mapSalt, new ChunkCoord(cx, cy), day, date.Season, date.MinuteOfDay, _woods, _terrain, _spawns);
                for (int i = 0; i < count; i++)
                {
                    this.Draw(world, _spawns[i], reduce);
                }
            }
        }
    }

    // The same world seed on every client, so the same chunk on the same day shows the same critters.
    private const long CritterSeed = 0x4E6F73756D5F414DL;

    private void Draw(WorldSpriteBatch world, CritterSpawn s, bool reduce)
    {
        int x = (s.TileX * _tileSize) + (s.SubX * _tileSize / CritterPlanner.SubUnits);
        int y = (s.TileY * _tileSize) + (s.SubY * _tileSize / CritterPlanner.SubUnits);
        if (!ViewCulling.Overlaps(x - Reach, y - Reach, 2 * Reach, 2 * Reach, _camera))
        {
            return;
        }

        double dx = x - _farmerX;
        double dy = y - _farmerY;
        double dist = Math.Sqrt((dx * dx) + (dy * dy));
        long clock = _clockMs;
        double t = clock / 1000.0;
        int ox = 0;
        int oy = 0;
        int sx;
        int sy;
        switch (s.Kind)
        {
            case CritterKind.Bird:
            {
                double flee = reduce ? 0 : Flee(dist, BirdFlee);
                if (flee > 0.05)
                {
                    // Flies off: away from the farmer and up.
                    ox = (int)(Away(dx, dist) * flee * 40);
                    oy = (int)((Away(dy, dist) * flee * 20) - (flee * 14));
                    (sx, sy) = (CellFly, RowAnimal);
                }
                else
                {
                    // Hops on the spot now and then.
                    oy = !reduce && (((clock / HopMs) + s.Id) % 12) == 0 ? -2 : 0;
                    (sx, sy) = (CellPerch, RowAnimal);
                }

                break;
            }

            case CritterKind.Squirrel:
            {
                double flee = reduce ? 0 : Flee(dist, SquirrelFlee);
                if (flee > 0.05)
                {
                    ox = (int)(Away(dx, dist) * flee * 32);
                    (sx, sy) = (CellRun, RowGround);
                }
                else
                {
                    (sx, sy) = (CellStand, RowGround);
                }

                break;
            }

            case CritterKind.Frog:
            {
                double flee = reduce ? 0 : Flee(dist, FrogFlee);
                if (flee > 0.05)
                {
                    ox = (int)(Away(dx, dist) * flee * 18);
                    oy = -(int)(flee * 10);
                    (sx, sy) = (CellFrogJump, RowAnimal);
                }
                else
                {
                    (sx, sy) = (CellFrogSit, RowAnimal);
                }

                break;
            }

            case CritterKind.Butterfly:
            {
                if (!reduce)
                {
                    ox = (int)(Math.Sin((t * 0.6) + s.Id) * 10);
                    oy = (int)((Math.Sin((t * 0.9) + (s.Id * 0.5)) * 6) - 10);
                }

                bool open = reduce || (((clock / WingMs) + s.Id) % 2) == 0;
                (sx, sy) = (open ? CellOpen : CellClosed, RowGround);
                break;
            }

            default:
            {
                // Firefly: hovers over the grass and glows in turn; reduced motion keeps it lit where it is.
                if (!reduce)
                {
                    ox = (int)(Math.Sin((t * 0.5) + s.Id) * 4);
                    oy = (int)((Math.Sin((t * 1.2) + s.Id) * 3) - 14);
                }

                bool lit = reduce || (((clock / BlinkMs) + s.Id) % 3) != 0;
                (sx, sy) = (lit ? CellLit : CellDim, RowAnimal);
                break;
            }
        }

        int left = x + ox - (Size / 2);
        int top = y + oy - (Size - 4);
        if (s.Kind == CritterKind.Firefly)
        {
            // A firefly glows, so it is drawn after the night tint, which would otherwise darken it.
            world.AddOverlay(_texture, sx, sy, Size, Size, left, top, y, x, s.Id);
        }
        else
        {
            world.Add(_texture, sx, sy, Size, Size, left, top, y, x, s.Id);
        }
    }

    // 0 at the edge of the radius, 1 on the farmer's feet.
    private static double Flee(double dist, double radius) => dist >= radius ? 0 : (radius - dist) / radius;

    // The component of a separation pointing away from the farmer, in -1..1; zero exactly on the farmer.
    private static double Away(double component, double dist) => dist < 1 ? 0 : component / dist;

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

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

/// <summary>Builds the <see cref="CritterDecorator"/> of every outdoor map; indoors and in the mines there is no wildlife.</summary>
internal sealed class CritterDecoratorProvider : IMapDecoratorProvider
{
    private const string SheetUrl = "assets/sprites/ambient.png";

    private readonly CalendarChannel? _calendar;

    /// <summary>Initializes the provider.</summary>
    /// <param name="calendar">The server's calendar; without it (offline) the maps have no wildlife.</param>
    public CritterDecoratorProvider(CalendarChannel? calendar)
    {
        _calendar = calendar;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        if (_calendar is null || spec.Indoors || spec.IsMineFloor)
        {
            return null;
        }

        int texture = await textures.LoadAsync(SheetUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new CritterDecorator(texture, _calendar, SaltOf(spec.Id), string.Equals(spec.Id, GameMaps.Woods, StringComparison.Ordinal), new MapCritterTerrain(map), map.TileWidth);
    }

    // FNV-1a: the same value in every process, unlike string.GetHashCode.
    internal static int SaltOf(string id)
    {
        uint h = 2166136261;
        foreach (char c in id)
        {
            h ^= c;
            h *= 16777619;
        }

        return (int)h;
    }
}

/// <summary>The ground of a map as the critters see it, read once when the map loads.</summary>
internal sealed class MapCritterTerrain : ICritterTerrain
{
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _clear;
    private readonly bool[] _water;

    /// <summary>Reads the ground and the water of a map.</summary>
    /// <param name="map">The map.</param>
    public MapCritterTerrain(TileMap map)
    {
        _width = map.Width;
        _height = map.Height;
        _clear = new bool[_width * _height];
        _water = new bool[_width * _height];
        TileLayer? back = map.FindLayer("Back");
        TileLayer? buildings = map.FindLayer("Buildings");
        if (back is null)
        {
            return;
        }

        for (int i = 0; i < _clear.Length; i++)
        {
            int gid = back.Gids[i];
            TileTraits traits = gid == 0 ? TileTraits.None : map.GetTraits(gid);
            _water[i] = (traits & TileTraits.Water) != 0;
            _clear[i] = gid != 0 && (traits & (TileTraits.Water | TileTraits.Impassable)) == 0 && (buildings is null || buildings.Gids[i] == 0);
        }
    }

    /// <inheritdoc/>
    public bool Clear(int tileX, int tileY) => this.InBounds(tileX, tileY) && _clear[(tileY * _width) + tileX];

    /// <inheritdoc/>
    public bool NearWater(int tileX, int tileY)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (this.InBounds(tileX + dx, tileY + dy) && _water[((tileY + dy) * _width) + tileX + dx])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;
}
