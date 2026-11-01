// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Dev;

/// <summary>
/// Opcodes of the Dev domain: tester tools that exist only when the host runs in the Development environment.
/// <para>Band <c>0xF000-0xF0FF</c>; requests <c>0xF000-0xF00F</c>, replies <c>0xF040-0xF04F</c>. The server registers the handler only when <c>DOTNET_ENVIRONMENT</c> is <c>Development</c>; in Production the opcode is unknown and the frame is dropped. The client sends it only from a build made with <c>-p:NosumDevTools=true</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class DevOpCode
{
    /// <summary>Gives the sender items and/or coins out of thin air (development only). Reply: <see cref="S2C.DevGrantResult"/>.</summary>
    public const ushort DevGrant = 0xF000;

    /// <summary>Moves the server's game clock forward by whole game days (development only, docs/adr/ADR-021 D7). Reply: <see cref="S2C.DevAdvanceClockResult"/>.</summary>
    public const ushort DevAdvanceClock = 0xF001;

    /// <summary>Moves the sender to a tile of a map through the normal arrival steps (development only, docs/adr/ADR-079). Reply: <see cref="S2C.DevTeleportResult"/>.</summary>
    public const ushort DevTeleport = 0xF002;

    /// <summary>Moves the server's game clock to the start of a later season, one or more times (development only, docs/adr/ADR-079). Reply: <see cref="S2C.DevSeasonResult"/>.</summary>
    public const ushort DevSeason = 0xF003;

    /// <summary>Pushes a level-up banner to the sender without changing the skill (development only, docs/adr/ADR-079). Reply: <see cref="S2C.DevLevelUpResult"/>.</summary>
    public const ushort DevLevelUp = 0xF004;

    /// <summary>Makes the sender's open fishing cast bite at once (development only, docs/adr/ADR-079). Reply: <see cref="S2C.DevFishingBiteResult"/>.</summary>
    public const ushort DevFishingBite = 0xF005;

    /// <summary>Pins the server's game clock, or releases it, so a QA run cannot see the season or day turn under it (development only, docs/adr/ADR-079 amendment 2026-11-01). Reply: <see cref="S2C.DevFreezeClockResult"/>.</summary>
    public const ushort DevFreezeClock = 0xF006;

    /// <summary>Skips the server's game clock to the next sunny day, the no-rain weather of a QA run (development only, docs/adr/ADR-079 amendment 2026-11-01). Reply: <see cref="S2C.DevSunnyDayResult"/>.</summary>
    public const ushort DevSunnyDay = 0xF007;

    /// <summary>Reply to <see cref="DevGrant"/>.</summary>
    public const ushort DevGrantResult = 0xF040;

    /// <summary>Reply to <see cref="DevAdvanceClock"/>.</summary>
    public const ushort DevAdvanceClockResult = 0xF041;

    /// <summary>Reply to <see cref="DevTeleport"/>.</summary>
    public const ushort DevTeleportResult = 0xF042;

    /// <summary>Reply to <see cref="DevSeason"/>.</summary>
    public const ushort DevSeasonResult = 0xF043;

    /// <summary>Reply to <see cref="DevLevelUp"/>.</summary>
    public const ushort DevLevelUpResult = 0xF044;

    /// <summary>Reply to <see cref="DevFishingBite"/>.</summary>
    public const ushort DevFishingBiteResult = 0xF045;

    /// <summary>Reply to <see cref="DevFreezeClock"/>.</summary>
    public const ushort DevFreezeClockResult = 0xF046;

    /// <summary>Reply to <see cref="DevSunnyDay"/>.</summary>
    public const ushort DevSunnyDayResult = 0xF047;
}
