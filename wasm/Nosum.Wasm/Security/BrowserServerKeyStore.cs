// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Nosum.Wasm.Socket.Security;

namespace Nosum.Wasm.Security;

/// <summary>
/// Remembers the development server key in the browser's <c>localStorage</c> (docs/adr/ADR-032 D1). Used only when no key is pinned in the boot
/// configuration. Storage that is blocked or cleared simply means the next visit trusts on first use again; it is never an error. Not used in the frame loop.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserServerKeyStore : IServerKeyStore
{
    private const string StorageKey = "nosum.serverKey";

    private readonly IJSRuntime _js;

    public BrowserServerKeyStore(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    /// <inheritdoc/>
    public async ValueTask<string?> GetAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey).ConfigureAwait(false);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync(string keyHex)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, keyHex).ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Storage is blocked: the key is simply not remembered.
        }
    }
}
