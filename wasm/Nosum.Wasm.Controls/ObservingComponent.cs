// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.AspNetCore.Components;
using Nosum.Wasm.ViewModels;

namespace Nosum.Wasm.Controls;

/// <summary>
/// Base of the controls: binds one view model and re-renders when it raises <see cref="IObservableViewModel.Changed"/>.
/// The view model raises it on a socket thread, so the render is marshalled onto the component's context
/// (docs/adr/ADR-003 D5, the blazor-ui rules), and the subscription is removed when the component goes away.
/// </summary>
/// <typeparam name="TViewModel">The view model type.</typeparam>
public abstract class ObservingComponent<TViewModel> : ComponentBase, IDisposable
    where TViewModel : class, IObservableViewModel
{
    private TViewModel? _subscribed;

    /// <summary>Gets or sets the view model the control shows.</summary>
    [Parameter, EditorRequired]
    public TViewModel Vm { get; set; } = default!;

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Removes the view model subscription.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.Unsubscribe();
        }
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribed, this.Vm))
        {
            return;
        }

        this.Unsubscribe();
        _subscribed = this.Vm;
        this.Vm.Changed += this.OnChanged;
    }

    private void OnChanged() => _ = this.InvokeAsync(this.StateHasChanged);

    private void Unsubscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= this.OnChanged;
            _subscribed = null;
        }
    }
}
