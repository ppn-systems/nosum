// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Nosum.Rendering;

/// <summary>
/// One frame's draw list, encoded into a preallocated <see cref="int"/> array so it crosses to
/// the backend as one byte span (docs/adr/ADR-002 D2). Nothing is allocated per frame.
/// </summary>
/// <remarks>
/// Layout (little-endian <c>int32</c>): header <c>[count, scale]</c>, then <c>count</c> draws of
/// <see cref="IntsPerCommand"/> values: <c>texture, srcX, srcY, srcW, srcH, dstX, dstY, flags,
/// tint</c>. Destination is in screen pixels before scaling; <c>tint</c> is <c>0xRRGGBBAA</c>,
/// <see cref="NoTint"/> meaning none. A draw with texture <see cref="BlackFill"/> fills a rectangle with black at the opacity in <c>tint</c>
/// (<see cref="FillBlack"/>). A draw with texture <see cref="LabelTexture"/> draws a pre-rendered text label (<see cref="DrawLabel"/>): <c>srcX</c> is the label id
/// the backend was given by <see cref="LabelRegistry"/>, the destination is the label's bottom centre. <c>renderer.js</c> decodes the same layout.
/// <para>
/// Appended for docs/adr/ADR-027 (the older commands keep their meaning): a draw with texture <see cref="FillTexture"/> fills a coloured rectangle (<see cref="FillRect"/>,
/// <c>tint</c> is <c>0xRRGGBBAA</c>); a draw with texture <see cref="LightTexture"/> adds a light source for this frame (<see cref="AddLight"/>: centre in <c>dstX, dstY</c>,
/// radius in <c>srcW</c>); a draw with texture <see cref="AmbientTexture"/> composites the ambient tint over everything drawn so far, cutting the frame's lights out of it
/// (<see cref="SetAmbient"/>: <c>srcX, srcY, srcW, srcH</c> hold red, green, blue and opacity).
/// </para>
/// <para>
/// Appended for docs/adr/ADR-039 (sound rides in the same buffer, so audio costs no crossing of its own): a draw with texture <see cref="SoundTexture"/> plays a sound once
/// (<see cref="PlaySound"/>: <c>srcX</c> sound id, <c>srcY</c> volume 0 to 100, <c>srcW</c> pan -100 to 100); <see cref="MusicTexture"/> sets the music track
/// (<see cref="SetMusic"/>: <c>srcX</c> track id, 0 for none); <see cref="AmbientLoopTexture"/> sets the ambient loop (<see cref="SetAmbientLoop"/>: <c>srcX</c> loop id, 0 for none,
/// <c>srcY</c> volume 0 to 100). They draw nothing; <c>renderer.js</c> hands them to <c>audio.js</c>.
/// </para>
/// </remarks>
public sealed class RenderCommandBuffer
{
    /// <summary>Values per draw command.</summary>
    public const int IntsPerCommand = 9;

    /// <summary>Header values before the first command.</summary>
    public const int HeaderInts = 2;

    /// <summary>Tint value meaning "draw unchanged".</summary>
    public const int NoTint = -1;

    /// <summary>Texture id of a command that fills a rectangle with black instead of drawing a sprite; <c>tint</c> then holds the opacity.</summary>
    public const int BlackFill = -2;

    /// <summary>Texture id of a command that draws a text label instead of a sprite; <c>srcX</c> then holds the label id (see <see cref="LabelRegistry"/>).</summary>
    public const int LabelTexture = -3;

    /// <summary>Texture id of a command that composites the ambient tint (docs/adr/ADR-027 D4); <c>srcX..srcH</c> then hold red, green, blue and opacity, 0 to 255.</summary>
    public const int AmbientTexture = -4;

    /// <summary>Texture id of a command that adds a light source for the ambient pass of this frame (docs/adr/ADR-027 D5); <c>srcW</c> then holds the radius and the destination the centre.</summary>
    public const int LightTexture = -5;

    /// <summary>Texture id of a command that fills a rectangle with a colour; <c>tint</c> then holds <c>0xRRGGBBAA</c> (rain and snow, docs/adr/ADR-027 D6).</summary>
    public const int FillTexture = -6;

    /// <summary>Texture id of a command that plays a sound once (docs/adr/ADR-039): <c>srcX</c> holds the sound id, <c>srcY</c> the volume and <c>srcW</c> the pan.</summary>
    public const int SoundTexture = -7;

    /// <summary>Texture id of a command that sets the music track (docs/adr/ADR-039): <c>srcX</c> holds the track id, 0 for silence.</summary>
    public const int MusicTexture = -8;

    /// <summary>Texture id of a command that sets the ambient loop (docs/adr/ADR-039): <c>srcX</c> holds the loop id, 0 for none, and <c>srcY</c> the volume.</summary>
    public const int AmbientLoopTexture = -9;

    /// <summary>
    /// Texture id of a command that opens a render target (docs/adr/ADR-086): <c>srcX</c> holds the target's texture id, <c>srcW</c> and <c>srcH</c>
    /// its size in pixels. The draws that follow go into that bitmap, unscaled and in its own pixels, until <see cref="EndTargetTexture"/>. A target
    /// is then drawn like any texture by its id.
    /// </summary>
    public const int BeginTargetTexture = -10;

    /// <summary>Texture id of a command that closes the render target opened by <see cref="BeginTargetTexture"/>.</summary>
    public const int EndTargetTexture = -11;

    /// <summary>
    /// Texture id of a command that draws one piece of a quest waypoint's edge arrow (docs/adr/ADR-096 D2): <c>srcX</c> is the direction (see
    /// <c>EdgeArrowDirection</c>), <c>srcY</c> 1 for the shadow and 0 for the arrow, <c>width</c> the box side in view pixels, (<c>dstX</c>, <c>dstY</c>) its top
    /// left, and the tint the colour. The renderer moves the arrow off the HUD, so the place is decided there.
    /// </summary>
    public const int EdgeArrowTexture = -12;

    /// <summary>
    /// Texture id of the command that records the local player's sprite box (docs/adr/ADR-097): <c>srcX</c> and <c>srcY</c> the box's top left,
    /// <c>width</c> and <c>height</c> its size, and (<c>dstX</c>, <c>dstY</c>) the feet, all in view pixels. Nothing is drawn; the renderer keeps
    /// the world labels off the box, except the player's own name plate, which is anchored at the feet.
    /// </summary>
    public const int PlayerBoxTexture = -13;

    /// <summary>Most light sources one frame may carry; further ones are dropped (docs/adr/ADR-027 D5).</summary>
    public const int MaxLights = 64;

    /// <summary>Flag: mirror the sprite horizontally.</summary>
    public const int FlipX = 1;

    /// <summary>
    /// Flag: wash the sprite toward white over its own silhouette, by the alpha byte of its <c>tint</c> (the monster hit flash,
    /// docs/adr/ADR-076). The colour bytes of the tint are ignored.
    /// </summary>
    public const int Whiten = 2;

    /// <summary>Packs a clockwise rotation in degrees into draw <c>flags</c> above <see cref="FlipX"/>.</summary>
    /// <param name="degrees">Clockwise rotation angle in degrees.</param>
    /// <returns>The encoded flag bits to combine with <see cref="FlipX"/>.</returns>
    public static int RotateDegrees(int degrees) => degrees << 8;

    private readonly int[] _data;
    private int _lights;

    /// <summary>Initializes a buffer.</summary>
    /// <param name="capacity">Maximum draws per frame.</param>
    public RenderCommandBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.Capacity = capacity;
        _data = new int[HeaderInts + (capacity * IntsPerCommand)];
    }

    /// <summary>Gets the maximum number of draws per frame.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of light sources recorded since <see cref="Begin"/>.</summary>
    public int LightCount => _lights;

    /// <summary>Gets the number of draws recorded since <see cref="Begin"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Starts a new frame.</summary>
    /// <param name="scale">Whole-number screen scale the backend applies to every draw.</param>
    public void Begin(int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        this.Count = 0;
        _lights = 0;
        _data[1] = scale;
    }

    /// <summary>
    /// Records one draw. Draws past <see cref="Capacity"/> are dropped.
    /// </summary>
    /// <param name="texture">Backend texture id.</param>
    /// <param name="srcX">Source left, pixels.</param>
    /// <param name="srcY">Source top, pixels.</param>
    /// <param name="width">Width, pixels.</param>
    /// <param name="height">Height, pixels.</param>
    /// <param name="dstX">Destination left, unscaled screen pixels.</param>
    /// <param name="dstY">Destination top, unscaled screen pixels.</param>
    /// <param name="flags">Draw flags such as <see cref="FlipX"/>.</param>
    /// <param name="tint">Multiply tint as <c>0xRRGGBBAA</c>, or <see cref="NoTint"/>.</param>
    public void Draw(int texture, int srcX, int srcY, int width, int height, int dstX, int dstY, int flags = 0, int tint = NoTint)
    {
        if (this.Count == this.Capacity)
        {
            return;
        }

        int i = HeaderInts + (this.Count * IntsPerCommand);
        _data[i] = texture;
        _data[i + 1] = srcX;
        _data[i + 2] = srcY;
        _data[i + 3] = width;
        _data[i + 4] = height;
        _data[i + 5] = dstX;
        _data[i + 6] = dstY;
        _data[i + 7] = flags;
        _data[i + 8] = tint;
        this.Count++;
    }

    /// <summary>Records the local player's sprite box and feet, in view pixels (see <see cref="PlayerBoxTexture"/>).</summary>
    /// <param name="x">Box left, view pixels.</param>
    /// <param name="y">Box top, view pixels.</param>
    /// <param name="width">Box width, view pixels.</param>
    /// <param name="height">Box height, view pixels.</param>
    /// <param name="feetX">Feet x, view pixels (the anchor of the player's own name plate).</param>
    /// <param name="feetY">Feet y, view pixels.</param>
    public void PlayerBox(int x, int y, int width, int height, int feetX, int feetY) => this.Draw(PlayerBoxTexture, x, y, width, height, feetX, feetY);

    /// <summary>
    /// Records a black rectangle over everything drawn so far, for fades between scenes. Nothing is recorded for zero opacity.
    /// </summary>
    /// <param name="opacity">0 (clear) to 255 (solid black).</param>
    /// <param name="width">Rectangle width, unscaled screen pixels.</param>
    /// <param name="height">Rectangle height, unscaled screen pixels.</param>
    public void FillBlack(int opacity, int width, int height)
    {
        if (opacity > 0)
        {
            this.Draw(BlackFill, 0, 0, width, height, 0, 0, 0, Math.Min(opacity, 255));
        }
    }

    /// <summary>Records a coloured rectangle in unscaled screen pixels. Nothing is recorded for a fully transparent colour or an empty rectangle.</summary>
    /// <param name="x">Left, unscaled screen pixels.</param>
    /// <param name="y">Top, unscaled screen pixels.</param>
    /// <param name="width">Width, unscaled screen pixels.</param>
    /// <param name="height">Height, unscaled screen pixels.</param>
    /// <param name="rgba">Colour as <c>0xRRGGBBAA</c>.</param>
    public void FillRect(int x, int y, int width, int height, int rgba)
    {
        if (width > 0 && height > 0 && (rgba & 0xFF) != 0)
        {
            this.Draw(FillTexture, 0, 0, width, height, x, y, 0, rgba);
        }
    }

    /// <summary>
    /// Records a light source for this frame's ambient pass: a hole in the tint around a point. At most <see cref="MaxLights"/> are kept; the caller culls to
    /// the camera first. Record the lights before <see cref="SetAmbient"/>.
    /// </summary>
    /// <param name="centerX">Centre x, unscaled screen pixels.</param>
    /// <param name="centerY">Centre y, unscaled screen pixels.</param>
    /// <param name="radius">Radius, unscaled screen pixels.</param>
    /// <returns><see langword="true"/> when the light was recorded.</returns>
    public bool AddLight(int centerX, int centerY, int radius)
    {
        if (radius <= 0 || _lights >= MaxLights || this.Count == this.Capacity)
        {
            return false;
        }

        _lights++;
        this.Draw(LightTexture, 0, 0, radius, 0, centerX, centerY);
        return true;
    }

    /// <summary>
    /// Records the ambient tint: one full-canvas composite over everything drawn so far, with the lights recorded before it cut out of it. Nothing is
    /// recorded for a fully transparent tint (daylight).
    /// </summary>
    /// <param name="red">Red, 0 to 255.</param>
    /// <param name="green">Green, 0 to 255.</param>
    /// <param name="blue">Blue, 0 to 255.</param>
    /// <param name="opacity">Opacity, 0 (none) to 255.</param>
    public void SetAmbient(int red, int green, int blue, int opacity)
    {
        if (opacity > 0)
        {
            this.Draw(AmbientTexture, Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255), Math.Min(opacity, 255), 0, 0);
        }
    }

    /// <summary>Records a one-shot sound. Nothing is recorded for an id below 1.</summary>
    /// <param name="soundId">Sound id the backend loaded (1 or more).</param>
    /// <param name="volume">Volume, 0 to 100 (clamped).</param>
    /// <param name="pan">Stereo position, -100 (left) to 100 (right), clamped.</param>
    public void PlaySound(int soundId, int volume, int pan)
    {
        if (soundId > 0)
        {
            this.Draw(SoundTexture, soundId, Math.Clamp(volume, 0, 100), Math.Clamp(pan, -100, 100), 0, 0, 0);
        }
    }

    /// <summary>Records the music track to play; the backend keeps it playing until the next such command.</summary>
    /// <param name="trackId">Track id, or 0 for silence.</param>
    public void SetMusic(int trackId) => this.Draw(MusicTexture, Math.Max(trackId, 0), 0, 0, 0, 0, 0);

    /// <summary>Opens a render target: the draws that follow go into its bitmap until <see cref="EndTarget"/> (docs/adr/ADR-086).</summary>
    /// <param name="targetId">The target's texture id; the backend creates or resizes its bitmap to <paramref name="width"/> by <paramref name="height"/>.</param>
    /// <param name="width">Width, pixels.</param>
    /// <param name="height">Height, pixels.</param>
    public void BeginTarget(int targetId, int width, int height) => this.Draw(BeginTargetTexture, targetId, 0, width, height, 0, 0);

    /// <summary>Closes the render target opened by <see cref="BeginTarget"/>.</summary>
    public void EndTarget() => this.Draw(EndTargetTexture, 0, 0, 0, 0, 0, 0);

    /// <summary>Records the ambient loop to play (rain); the backend keeps it playing until the next such command.</summary>
    /// <param name="loopId">Loop id, or 0 for none.</param>
    /// <param name="volume">Volume, 0 to 100 (clamped).</param>
    public void SetAmbientLoop(int loopId, int volume) => this.Draw(AmbientLoopTexture, Math.Max(loopId, 0), Math.Clamp(volume, 0, 100), 0, 0, 0, 0);

    /// <summary>
    /// Records a text label (a nickname above a head), centred on <paramref name="centerX"/> with its bottom edge at <paramref name="bottomY"/>.
    /// The text itself never crosses per frame: it was uploaded once with <see cref="LabelRegistry"/> and only its id is recorded here.
    /// </summary>
    /// <param name="labelId">Label id from <see cref="LabelRegistry"/>.</param>
    /// <param name="centerX">Centre x, unscaled screen pixels.</param>
    /// <param name="bottomY">Bottom y, unscaled screen pixels.</param>
    public void DrawLabel(int labelId, int centerX, int bottomY)
    {
        if (labelId > 0)
        {
            this.Draw(LabelTexture, labelId, 0, 0, 0, centerX, bottomY);
        }
    }

    /// <summary>
    /// Returns the encoded frame (header plus recorded draws) as bytes over the internal array.
    /// </summary>
    /// <returns>The frame bytes; valid until the next <see cref="Begin"/>.</returns>
    public Span<byte> Encode()
    {
        _data[0] = this.Count;
        return MemoryMarshal.AsBytes(_data.AsSpan(0, HeaderInts + (this.Count * IntsPerCommand)));
    }
}
