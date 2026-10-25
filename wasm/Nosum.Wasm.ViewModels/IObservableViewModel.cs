// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels;

/// <summary>A view model a control can follow: it says when what it shows has changed. May be raised on any thread.</summary>
public interface IObservableViewModel
{
    /// <summary>Raised when the control must render again.</summary>
    event Action? Changed;
}
