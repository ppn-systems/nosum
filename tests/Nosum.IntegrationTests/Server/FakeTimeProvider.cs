// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.IntegrationTests.Server;

/// <summary>A clock tests move by hand.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
