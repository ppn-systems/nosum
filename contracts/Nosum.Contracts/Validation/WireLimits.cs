// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Validation;

/// <summary>
/// Sanity bounds for client-supplied packet fields (docs/adr/ADR-032 D3). They are deliberately looser than the game rules
/// (a nickname is 16 characters, a chat line 160): the rules still answer with a status code, while a value outside these
/// bounds can only come from a hostile or broken client, so the server drops the packet before any handler runs.
/// </summary>
public static class WireLimits
{
    /// <summary>Longest short text: e-mail address, nickname, map id, co-op name.</summary>
    public const int ShortText = 64;

    /// <summary>Longest provider ID token (a signed JWT: Microsoft's are about 2 KB).</summary>
    public const int IdToken = 8192;

    /// <summary>Longest session token (a 32-byte token is 43 characters in base64url).</summary>
    public const int Token = 128;

    /// <summary>Longest chat line.</summary>
    public const int ChatText = 400;

    /// <summary>Largest item, goal, shop, object or recipe id.</summary>
    public const int MaxId = 1_000_000;

    /// <summary>Largest item quantity in one request.</summary>
    public const int MaxQuantity = 1_000_000;

    /// <summary>Largest coin amount or price in one request.</summary>
    public const long MaxCoins = 1_000_000_000_000L;

    /// <summary>Smallest accepted tile coordinate.</summary>
    public const int TileMin = -4096;

    /// <summary>Largest accepted tile coordinate.</summary>
    public const int TileMax = 4096;

    /// <summary>Largest absolute world coordinate in pixels.</summary>
    public const float WorldMax = 100_000f;

    /// <summary>Largest result page number.</summary>
    public const int MaxPage = 100_000;

    /// <summary>Most entries in a filter list.</summary>
    public const int MaxFilterEntries = 32;

    /// <summary>Most lines in a trade offer.</summary>
    public const int MaxOfferLines = 16;

    /// <summary>Most listings on one browse page (docs/adr/ADR-101 amendment 2026-11-01). The client asks for the rows its panel has room for; the server clamps to this.</summary>
    public const int MaxListingsPageSize = 20;
}
