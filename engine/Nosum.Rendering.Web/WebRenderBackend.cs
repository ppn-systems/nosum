// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Rendering.Web;

/// <summary>
/// Browser <see cref="IRenderBackend"/>: passes each frame's command buffer to the
/// <c>renderer.js</c> module in one synchronous <c>[JSImport]</c> call.
/// </summary>
/// <remarks>
/// The buffer crosses as a <c>MemoryView</c> over the .NET memory, without a copy and without
/// JSON. A <c>MemoryView</c> made from a <see cref="Span{T}"/> is only valid during the call,
/// so the JS side must decode and draw before it returns and must not keep the view.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class WebRenderBackend : IRenderBackend
{
    /// <summary>The name the JS module is registered under.</summary>
    public const string ModuleName = "nosum-renderer";

    /// <summary>The URL of the JS module, served as a static web asset of this library.</summary>
    public const string ModuleUrl = "/_content/Nosum.Rendering.Web/renderer.js";

    /// <summary>
    /// Loads the JS module. Call once at startup, before the first <see cref="Submit"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the module download.</param>
    /// <returns>The loaded module.</returns>
    public static Task<JSObject> ImportModuleAsync(CancellationToken cancellationToken = default)
        => JSHost.ImportAsync(ModuleName, ModuleUrl, cancellationToken);

    /// <inheritdoc/>
    public void Submit(Span<byte> commands) => SubmitFrame(commands);

    [JSImport("submitFrame", ModuleName)]
    private static partial void SubmitFrame([JSMarshalAs<JSType.MemoryView>] Span<byte> commands);
}
