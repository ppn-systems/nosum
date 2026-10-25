// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// A platform renderer that draws one frame from an encoded command buffer.
/// </summary>
/// <remarks>
/// The whole frame is handed over in a single call so a backend that crosses an interop
/// boundary (the browser backend in <c>Nosum.Rendering.Web</c>) pays for one crossing per
/// frame, never one per sprite. The buffer is only valid for the duration of the call.
/// </remarks>
public interface IRenderBackend
{
    /// <summary>
    /// Draws one frame.
    /// </summary>
    /// <param name="commands">The encoded render commands for the frame; valid only during this call.</param>
    void Submit(Span<byte> commands);
}
