// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>A piece that was set down says so on the message line (docs/adr/ADR-104 D3), in both languages.</summary>
public sealed class PlaceDoneKeyTests
{
    [Fact]
    public void PlaceDone_IsInBothLanguages()
    {
        HashSet<string> vi = SocialClientFixtures.Keys("Language.resx");
        HashSet<string> en = SocialClientFixtures.Keys("Language.en.resx");
        vi.Should().Contain("Place_Done");
        en.Should().Contain("Place_Done");
    }
}
