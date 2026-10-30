// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Fishing;

/// <summary>
/// What the fishing scene needs each frame, read in one step: the phase, the bite window's time left and length, and the tile the line went
/// into (docs/adr/ADR-078 polish). A value type, so reading it per frame allocates nothing.
/// </summary>
/// <param name="Phase">Where the banner is in the cast.</param>
/// <param name="BarMs">Milliseconds of the bite window left; zero outside the window.</param>
/// <param name="WindowMs">The length of the bite window of this cast, in milliseconds.</param>
/// <param name="CastTileX">Tile x the line went into.</param>
/// <param name="CastTileY">Tile y the line went into.</param>
public readonly record struct FishingFrame(FishingPhase Phase, int BarMs, int WindowMs, int CastTileX, int CastTileY);
