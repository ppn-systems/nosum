// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Looks;

/// <summary>Why a look change was refused.</summary>
public enum LookVerdict
{
    /// <summary>The change is allowed.</summary>
    Ok = 0,

    /// <summary>The skin id is not in the catalogue.</summary>
    InvalidSkin = 1,

    /// <summary>The hair style id is not in the catalogue.</summary>
    InvalidHairStyle = 2,

    /// <summary>The hair dye id is not in the catalogue.</summary>
    InvalidHairDye = 3,

    /// <summary>The item is not clothing.</summary>
    NotWearable = 4,
}

/// <summary>
/// The avatar parts a player may choose from, as id ranges. Ids start at 1 and are append-only; what each id looks like (sheet frame,
/// colour) is the client's catalogue (docs/adr/ADR-006 D6), so art can change without touching the server.
/// </summary>
public static class LookCatalog
{
    /// <summary>Number of skin tones.</summary>
    public const int SkinCount = 4;

    /// <summary>Number of hair styles.</summary>
    public const int HairStyleCount = 8;

    /// <summary>Number of hair dyes.</summary>
    public const int HairDyeCount = 6;

    /// <summary>Checks a choice of avatar parts.</summary>
    /// <param name="skinId">Skin tone id.</param>
    /// <param name="hairStyleId">Hair style id.</param>
    /// <param name="hairDyeId">Hair dye id.</param>
    /// <returns><see cref="LookVerdict.Ok"/> or the first invalid part.</returns>
    public static LookVerdict ValidateAppearance(int skinId, int hairStyleId, int hairDyeId)
    {
        if (skinId < 1 || skinId > SkinCount)
        {
            return LookVerdict.InvalidSkin;
        }

        if (hairStyleId < 1 || hairStyleId > HairStyleCount)
        {
            return LookVerdict.InvalidHairStyle;
        }

        return hairDyeId < 1 || hairDyeId > HairDyeCount ? LookVerdict.InvalidHairDye : LookVerdict.Ok;
    }
}
