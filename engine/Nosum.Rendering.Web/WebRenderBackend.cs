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
    /// Loads the JS module. Call once at startup, before any other member.
    /// </summary>
    /// <param name="cancellationToken">Cancels the module download.</param>
    /// <returns>The loaded module.</returns>
    public static Task<JSObject> ImportModuleAsync(CancellationToken cancellationToken = default)
        => JSHost.ImportAsync(ModuleName, ModuleUrl, cancellationToken);

    /// <summary>Downloads an image and registers it under a texture id.</summary>
    /// <param name="id">Texture id used by render commands.</param>
    /// <param name="url">Image URL.</param>
    /// <returns>A task that completes when the texture is ready.</returns>
    public static Task LoadTextureAsync(int id, string url) => LoadTexture(id, url);

    /// <summary>
    /// Starts the frame loop: every animation frame JS calls the exported static method
    /// <paramref name="exportPath"/> of <paramref name="assemblyName"/> with
    /// <c>(double timeMs, int packedKeys, int canvasWidth, int canvasHeight)</c>, where
    /// <c>packedKeys</c> holds the held directions in the low byte and the keys pressed since the last frame above it (<c>Nosum.Core.InputPacking</c>).
    /// </summary>
    /// <param name="canvasId">Id of the canvas element.</param>
    /// <param name="assemblyName">Assembly holding the export, e.g. <c>Nosum.Wasm.dll</c>.</param>
    /// <param name="exportPath">Full name of the exported method, e.g. <c>Nosum.Wasm.Game.GameLoop.Frame</c>.</param>
    /// <returns>A task that completes once the loop is running.</returns>
    public static Task StartAsync(string canvasId, string assemblyName, string exportPath) => Start(canvasId, assemblyName, exportPath);

    /// <summary>Uploads a text label (a nickname) once; the backend renders it into a cached bitmap and frames draw it by id (<see cref="LabelRegistry"/>).</summary>
    /// <param name="id">Label id.</param>
    /// <param name="text">The text.</param>
    /// <param name="style">Style number: 0 normal, 1 the local player's.</param>
    public static void UploadLabel(int id, string text, int style) => RegisterLabel(id, text, style);

    /// <inheritdoc/>
    public void Submit(Span<byte> commands) => SubmitFrame(commands);

    [JSImport("submitFrame", ModuleName)]
    private static partial void SubmitFrame([JSMarshalAs<JSType.MemoryView>] Span<byte> commands);

    [JSImport("registerLabel", ModuleName)]
    private static partial void RegisterLabel(int id, string text, int style);

    [JSImport("loadTexture", ModuleName)]
    private static partial Task LoadTexture(int id, string url);

    [JSImport("start", ModuleName)]
    private static partial Task Start(string canvasId, string assemblyName, string exportPath);
}
