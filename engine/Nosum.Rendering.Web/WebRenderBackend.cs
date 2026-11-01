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

    /// <summary>Sets the keys bound to the actions; call when the player changes them (never from the frame).</summary>
    /// <param name="bindings">Every action as <c>Action=Code;Action=Code</c>, the action names being <c>Nosum.Wasm.ViewModels.Settings.GameAction</c> and the codes browser key codes.</param>
    public static void SetBindings(string bindings) => SetKeyBindings(bindings);

    /// <summary>Downloads and decodes one audio file once, at load (docs/adr/ADR-039). A file that is missing or cannot be decoded leaves its id silent; it never throws.</summary>
    /// <param name="kind">0 for a one-shot sound, 1 for an ambient loop, 2 for a music track.</param>
    /// <param name="id">The sound, loop or track id used by the audio commands of the frame.</param>
    /// <param name="url">File URL.</param>
    /// <param name="gainDb">The gain the file gets once, when it is decoded, in decibels (the mix table, docs/adr/ADR-090); 0 for none.</param>
    /// <returns>A task that completes when the file is decoded (<see langword="true"/>) or was skipped (<see langword="false"/>).</returns>
    public static Task<bool> LoadAudioAsync(int kind, int id, string url, double gainDb = 0) => LoadAudio(kind, id, url, gainDb);

    /// <summary>Sets the music and sound effect gains from the settings; call when the player changes them (never from the frame).</summary>
    /// <param name="musicGain">Music gain, 0 to 1 (<see cref="VolumeCurve"/>, 0 when muted).</param>
    /// <param name="soundGain">Sound effect gain, 0 to 1 (0 when muted).</param>
    public static void SetLevels(double musicGain, double soundGain) => SetAudioLevels(musicGain, soundGain);

    /// <summary>Chooses the sound a click on any button of the interface plays, in the browser, without a call into .NET.</summary>
    /// <param name="soundId">The sound id, or 0 for none.</param>
    public static void SetUiClick(int soundId) => SetUiClickSound(soundId);

    /// <inheritdoc/>
    public void Submit(Span<byte> commands) => SubmitFrame(commands);

    [JSImport("submitFrame", ModuleName)]
    private static partial void SubmitFrame([JSMarshalAs<JSType.MemoryView>] Span<byte> commands);

    [JSImport("setKeyBindings", ModuleName)]
    private static partial void SetKeyBindings(string bindings);

    [JSImport("registerLabel", ModuleName)]
    private static partial void RegisterLabel(int id, string text, int style);

    [JSImport("loadAudio", ModuleName)]
    private static partial Task<bool> LoadAudio(int kind, int id, string url, double gainDb);

    [JSImport("setAudioLevels", ModuleName)]
    private static partial void SetAudioLevels(double musicGain, double soundGain);

    [JSImport("setUiClickSound", ModuleName)]
    private static partial void SetUiClickSound(int soundId);

    [JSImport("loadTexture", ModuleName)]
    private static partial Task LoadTexture(int id, string url);

    [JSImport("start", ModuleName)]
    private static partial Task Start(string canvasId, string assemblyName, string exportPath);
}
