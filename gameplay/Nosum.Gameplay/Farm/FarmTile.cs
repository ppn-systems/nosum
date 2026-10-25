// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Farm;

/// <summary>
/// The state of one tilled tile. A tile that was never tilled has no <see cref="FarmTile"/>.
/// All times are Unix milliseconds on the server's clock; growth is computed from them on demand
/// (<see cref="FarmRules"/>), never simulated (docs/adr/ADR-003 D4).
/// </summary>
/// <param name="CropId">The planted crop, or zero for bare soil.</param>
/// <param name="PlantedAtMs">When the crop was planted; zero without a crop.</param>
/// <param name="GrowthMs">Watered growth time settled up to <paramref name="SettledAtMs"/>.</param>
/// <param name="SettledAtMs">The moment <paramref name="GrowthMs"/> was last brought up to date.</param>
/// <param name="WetUntilMs">The soil counts as watered until this moment; zero or earlier than now means dry.</param>
public readonly record struct FarmTile(int CropId, long PlantedAtMs, long GrowthMs, long SettledAtMs, long WetUntilMs)
{
    /// <summary>Gets bare, dry, freshly tilled soil.</summary>
    public static FarmTile Bare => default;

    /// <summary>Gets whether a crop grows on the tile.</summary>
    public bool HasCrop => this.CropId != 0;
}
