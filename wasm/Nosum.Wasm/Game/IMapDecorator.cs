// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// Something a map draws besides its tile layers: buildings and props, and later crops, soil or animals.
/// This is the one place map-specific drawing plugs into <see cref="MapScene"/>, so the scene itself knows
/// nothing about farming. Created per map by an <see cref="IMapDecoratorProvider"/>. Frame path: no allocation, no await.
/// </summary>
internal interface IMapDecorator
{
    /// <summary>Tells the decorator what the camera sees this frame, before <see cref="AddToWorld"/>, so it can skip what is off screen.</summary>
    /// <param name="camera">The camera.</param>
    void SetView(in Camera2D camera)
    {
    }

    /// <summary>Provides the unified frame clock before drawing or adding world sprites.</summary>
    /// <param name="clock">The frame clock snapshot.</param>
    void SetClock(in FrameClock clock)
    {
    }

    /// <summary>Tells the decorator where the farmer stands this frame, in world pixels, for what keeps its distance from the farmer (wildlife).</summary>
    /// <param name="x">The farmer's x, world pixels.</param>
    /// <param name="y">The farmer's y, world pixels.</param>
    void SetFarmer(double x, double y)
    {
    }

    /// <summary>Draws over the ground layers and below the characters (soil, crops lying on the ground).</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="timeMs">Animation clock, milliseconds.</param>
    void DrawOnGround(RenderCommandBuffer commands, in Camera2D camera, long timeMs)
    {
    }

    /// <summary>Adds things that stand upright to the depth-sorted batch the characters are in.</summary>
    /// <param name="world">The world batch of this frame.</param>
    void AddToWorld(WorldSpriteBatch world)
    {
    }

    /// <summary>Draws over the characters and below the map's front layers (roofs).</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    void DrawInFront(RenderCommandBuffer commands, in Camera2D camera)
    {
    }
}
