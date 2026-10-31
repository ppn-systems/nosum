// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Inventory;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Backpack;

/// <summary>
/// View model of the backpack page the tailor opens (docs/adr/ADR-081): the size now, the next size with its price, and the buy button. The server checks
/// the counter, the coins and the size for every purchase; this page only shows what the server last said. After a purchase the inventory is read again,
/// so the new rows of the bag appear at once.
/// </summary>
public sealed class BackpackViewModel : PanelViewModel, IDisposable
{
    private readonly BackpackChannel _channel;
    private readonly InventoryChannel _inventory;
    private BackpackInfo _info;
    private bool _buying;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Talks to the tailor about the backpack.</param>
    /// <param name="inventory">The bag: re-read after a purchase.</param>
    /// <param name="text">UI text.</param>
    public BackpackViewModel(BackpackChannel channel, InventoryChannel inventory, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(inventory);

        _channel = channel;
        _inventory = inventory;
        channel.Updated += this.OnUpdated;
    }

    /// <summary>Gets the line that says how many slots the backpack holds now.</summary>
    public string SizeText => this.Text("Backpack_Size", _info.Capacity > 0 ? _info.Capacity : _inventory.Capacity);

    /// <summary>Gets whether the backpack is already the largest size.</summary>
    public bool IsLargest => _info.NextCapacity == 0;

    /// <summary>Gets the line that offers the next size and its price; the largest-size line when there is none.</summary>
    public string OfferText => this.IsLargest ? this.Text("Backpack_Max") : this.Text("Backpack_Next", _info.NextCapacity, Number(_info.NextPrice));

    /// <summary>Gets the text of the buy button.</summary>
    public string UpgradeLabel => this.Text("Backpack_Upgrade");

    /// <summary>Gets the line that tells what a purchase does.</summary>
    public string HintText => this.Text("Backpack_Hint");

    /// <summary>Gets whether the buy button can be pressed: there is a larger size and no purchase is waiting for its answer.</summary>
    public bool CanUpgrade => !this.IsLargest && !_buying;

    /// <summary>Reads the size and the next upgrade (call when the page opens).</summary>
    public void Open()
    {
        this.ClearStatus();
        _channel.Load();
        this.Raise();
    }

    /// <summary>Buys the next size.</summary>
    public void Upgrade()
    {
        if (!this.CanUpgrade)
        {
            return;
        }

        this.ClearStatus();
        _buying = true;
        this.Raise();
        _channel.Upgrade();
    }

    /// <inheritdoc/>
    public void Dispose() => _channel.Updated -= this.OnUpdated;

    private void OnUpdated(BackpackInfo info)
    {
        bool bought = _buying;
        _buying = false;
        _info = info;
        if (info.Status != BackpackStatus.Ok)
        {
            this.SetStatus(this.Text("Backpack_Status_" + info.Status));
            return;
        }

        if (bought)
        {
            // The bag now has the new rows: read it again, the grid follows the size it reports.
            _inventory.Load();
            this.SetStatus(this.Text("Backpack_Status_Ok"), good: true);
            return;
        }

        this.Raise();
    }
}
