// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.UnitTests.Wasm;

/// <summary>Builds the pieces the chat, friends and mail view models need, without a real socket.</summary>
internal static class SocialClientFixtures
{
    public static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The look channel only subscribes to the fake socket, which dies with the test.")]
    public static NicknameFormatter Names(FakeGameSocket socket) => new(Localizer<Language>(), new LookChannel(socket, () => 0));

    public static ItemDescriber Items() => new(new NoIcons(), Localizer<Catalogue>(), Localizer<Language>());

    /// <summary>The keys of one resource file of the view-model project.</summary>
    public static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    /// <summary>Runs with the UI culture set to <paramref name="name"/>, then restores it.</summary>
    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
            return true;
        }
    }
}
