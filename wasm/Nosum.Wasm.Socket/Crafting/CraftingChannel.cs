// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;

namespace Nosum.Wasm.Socket.Crafting;

/// <summary>
/// The crafting side of the connection (docs/adr/ADR-024 D1, D7): reads which recipes the server says are unlocked and sends craft requests. The server checks the
/// unlock and the items and moves them in one step; the result reaches the inventory mirror as an ordinary inventory update.
/// </summary>
public sealed class CraftingChannel
{
    private readonly IGameSocket _socket;
    private readonly Lock _gate = new();
    private HashSet<int> _unlocked = [];
    private bool _loaded;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public CraftingChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the unlocked set was (re)loaded. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a craft request. May run on any thread.</summary>
    public event Action<CraftStatus>? Completed;

    /// <summary>Raised when the server crafted the recipe: the recipe id and how many times it was crafted.</summary>
    public event Action<int, int>? Crafted;

    /// <summary>Gets whether the recipe book was read at least once.</summary>
    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    /// <summary>Tells whether the server said a recipe is unlocked.</summary>
    /// <param name="recipeId">Recipe id.</param>
    /// <returns><see langword="true"/> when unlocked.</returns>
    public bool IsUnlocked(int recipeId)
    {
        lock (_gate)
        {
            return _unlocked.Contains(recipeId);
        }
    }

    /// <summary>Reads the recipe book. Call when the crafting page opens and after a level-up.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Asks the server to craft a recipe.</summary>
    /// <param name="recipeId">Recipe id.</param>
    /// <param name="times">How many crafts, 1 to 99.</param>
    public void Craft(int recipeId, int times) => _ = this.CraftAsync(recipeId, times);

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the page opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetRecipeBook> request = PacketFactory<GetRecipeBook>.Acquire();
            RecipeBook book = await _socket.RequestAsync<RecipeBook>(request.Value).ConfigureAwait(false);
            lock (_gate)
            {
                _unlocked = [.. book.Unlocked];
                _loaded = true;
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task CraftAsync(int recipeId, int times)
    {
        try
        {
            using PacketScope<Craft> request = PacketFactory<Craft>.Acquire();
            request.Value.RecipeId = (ushort)recipeId;
            request.Value.Times = (byte)times;
            CraftResult result = await _socket.RequestAsync<CraftResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
            if (result.Status == CraftStatus.Ok)
            {
                this.Crafted?.Invoke(recipeId, times);
            }
        }
        catch (Exception)
        {
            // Connection dropped; nothing was crafted.
        }
    }
}
