// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Ambient;

/// <summary>The wildlife of the outdoor maps (docs/adr/ADR-071). Birds, frogs and squirrels sit or perch; butterflies and fireflies drift.</summary>
public enum CritterKind : byte
{
    /// <summary>A bird on the ground or a post; any season, by day.</summary>
    Bird = 0,

    /// <summary>A butterfly; spring and summer, by day.</summary>
    Butterfly = 1,

    /// <summary>A firefly; summer nights only.</summary>
    Firefly = 2,

    /// <summary>A frog; any time, on a tile next to water.</summary>
    Frog = 3,

    /// <summary>A squirrel; by day, in the Woods only.</summary>
    Squirrel = 4,
}

/// <summary>
/// One planned critter: the tile it is on and the offset inside that tile in sixteenths of a tile, plus an id stable for the day and
/// place, which the view uses to give each critter its own motion phase.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="TileX">The tile column, in map tiles.</param>
/// <param name="TileY">The tile row, in map tiles.</param>
/// <param name="SubX">The horizontal offset inside the tile, 0 to 15 sixteenths.</param>
/// <param name="SubY">The vertical offset inside the tile, 0 to 15 sixteenths.</param>
/// <param name="Id">A non-negative id, the same on every client for the same day and place.</param>
public readonly record struct CritterSpawn(CritterKind Kind, int TileX, int TileY, int SubX, int SubY, int Id);

/// <summary>The ground a critter may be placed on, read from the map by the client.</summary>
public interface ICritterTerrain
{
    /// <summary>Whether a critter may stand on this tile: a ground tile that is neither water, blocked nor built on.</summary>
    /// <param name="tileX">The tile column.</param>
    /// <param name="tileY">The tile row.</param>
    bool Clear(int tileX, int tileY);

    /// <summary>Whether water lies within two tiles of this tile.</summary>
    /// <param name="tileX">The tile column.</param>
    /// <param name="tileY">The tile row.</param>
    bool NearWater(int tileX, int tileY);
}

/// <summary>
/// Plans the critters of one map chunk on one game day (docs/adr/ADR-071 D2). The plan is a pure function of the world seed, the map, the chunk,
/// the day, the season, the time of day and the ground, so every client that sees the same chunk on the same day sees the same critters. It is
/// allocation-free: the caller passes the span to fill.
/// </summary>
public static class CritterPlanner
{
    /// <summary>The most critters planned for one chunk, over all kinds.</summary>
    public const int MaxPerChunk = 4;

    /// <summary>The first minute of the day that counts as night for fireflies (6:00 is dawn and light).</summary>
    public const int DawnMinute = 6 * 60;

    /// <summary>The first minute of the evening that counts as night (19:00).</summary>
    public const int DuskMinute = 19 * 60;

    /// <summary>Sixteenths of a tile per tile: the scale of <see cref="CritterSpawn.SubX"/> and <see cref="CritterSpawn.SubY"/>.</summary>
    public const int SubUnits = 16;

    /// <summary>Whether the minute of the day is night for the critters.</summary>
    /// <param name="minuteOfDay">The game minute, 0 to 1439.</param>
    public static bool IsNight(int minuteOfDay) => minuteOfDay < DawnMinute || minuteOfDay >= DuskMinute;

    /// <summary>Plans the critters of a chunk for a day.</summary>
    /// <param name="seed">The world seed, the same for every client.</param>
    /// <param name="mapSalt">A value derived from the map's id, so two maps do not repeat each other.</param>
    /// <param name="chunk">The chunk, in chunk units (16 tiles each).</param>
    /// <param name="day">The game day number.</param>
    /// <param name="season">The season.</param>
    /// <param name="minuteOfDay">The game minute of the day.</param>
    /// <param name="woods">Whether the map is the Woods, where squirrels live.</param>
    /// <param name="terrain">The ground of the map.</param>
    /// <param name="into">Where the critters are written; at most <see cref="MaxPerChunk"/> and no more than its length.</param>
    /// <returns>How many entries of <paramref name="into"/> were written.</returns>
    public static int Plan(long seed, int mapSalt, ChunkCoord chunk, int day, Season season, int minuteOfDay, bool woods, ICritterTerrain terrain, Span<CritterSpawn> into)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        bool night = IsNight(minuteOfDay);
        int limit = Math.Min(MaxPerChunk, into.Length);
        int written = 0;
        for (int kind = 0; kind < KindCount && written < limit; kind++)
        {
            CritterKind k = (CritterKind)kind;
            int count = CountOf(k, season, night, woods, Hash(seed, mapSalt, chunk, day, kind * 8));
            for (int n = 0; n < count && written < limit; n++)
            {
                ulong h = Hash(seed, mapSalt, chunk, day, (kind * 8) + 1 + n);
                int tileX = (chunk.X * ChunkCoord.SizeTiles) + (int)(h % ChunkCoord.SizeTiles);
                int tileY = (chunk.Y * ChunkCoord.SizeTiles) + (int)((h >> 8) % ChunkCoord.SizeTiles);
                if (!Fits(k, tileX, tileY, terrain))
                {
                    continue;
                }

                int subX = (int)((h >> 16) % SubUnits);
                int subY = (int)((h >> 24) % SubUnits);
                into[written++] = new CritterSpawn(k, tileX, tileY, subX, subY, (int)((h >> 32) & 0x7FFF_FFFF));
            }
        }

        return written;
    }

    private const int KindCount = 5;

    // How many of a kind the chunk holds today: a bird most days, butterflies and fireflies in their seasons, frogs anywhere, squirrels in the Woods.
    private static int CountOf(CritterKind kind, Season season, bool night, bool woods, ulong h) => kind switch
    {
        CritterKind.Bird => !night && h % 3 != 0 ? 1 + (int)((h >> 4) % 2) : 0,
        CritterKind.Butterfly => !night && (season == Season.Spring || season == Season.Summer) ? (int)(h % 3) : 0,
        CritterKind.Firefly => night && season == Season.Summer ? (int)(h % 4) : 0,
        CritterKind.Frog => (int)(h % 3),
        CritterKind.Squirrel => woods && !night ? (int)(h % 3) : 0,
        _ => 0,
    };

    private static bool Fits(CritterKind kind, int tileX, int tileY, ICritterTerrain terrain) => kind switch
    {
        CritterKind.Frog => terrain.Clear(tileX, tileY) && terrain.NearWater(tileX, tileY),
        CritterKind.Firefly or CritterKind.Butterfly => true,
        _ => terrain.Clear(tileX, tileY),
    };

    // SplitMix64 over the inputs: the same values on every client, no allocation.
    private static ulong Hash(long seed, int mapSalt, ChunkCoord chunk, int day, int lane)
    {
        ulong h = Mix((ulong)seed ^ (uint)mapSalt);
        h = Mix(h ^ (((ulong)(uint)chunk.X << 32) | (uint)chunk.Y));
        return Mix(h ^ (((ulong)(uint)day << 32) | (uint)lane));
    }

    private static ulong Mix(ulong h)
    {
        h ^= h >> 30;
        h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 27;
        h *= 0x94D049BB133111EBUL;
        return h ^ (h >> 31);
    }
}
