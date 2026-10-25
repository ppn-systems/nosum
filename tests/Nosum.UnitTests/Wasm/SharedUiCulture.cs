// Copyright (c) 2026 PPN Systems. All rights reserved.

using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// Tests that touch the process-wide UI language run one at a time: <c>UiCulture.Apply</c> sets the default culture of every thread and raises a
/// static event that rebuilds every live view model listening to it, so a parallel test could see another test's language.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedUiCulture
{
    /// <summary>The collection name.</summary>
    public const string Name = "UiCulture";
}
