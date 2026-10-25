// Copyright (c) 2026 PPN Systems. All rights reserved.

using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Nalix keeps its services in process-wide singletons, so tests that build the host must not run
/// in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NalixHostSerial
{
    /// <summary>Collection name.</summary>
    public const string Name = "NalixHost";
}
