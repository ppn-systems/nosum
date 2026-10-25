// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Player;

namespace Nosum.Wasm.Game;

/// <summary>
/// Maps the nicknames of the players on the map to label ids the frame can draw (<see cref="RenderCommandBuffer.DrawLabel"/>). Names are
/// uploaded to the browser once, here, when the server's look for a player arrives (<see cref="LookChannel.Changed"/>) - never inside the frame,
/// which only does a dictionary lookup. A guest's neutral name is shown with the localized prefix.
/// </summary>
internal sealed class NicknameLabels : IDisposable
{
    private const int NormalStyle = 0;
    private const int OwnStyle = 1;

    private readonly LabelRegistry _registry;
    private readonly LookChannel _looks;
    private readonly NicknameFormatter _names;
    private readonly Dictionary<string, int> _byNickname = new(StringComparer.Ordinal);

    /// <summary>Starts following a look channel.</summary>
    /// <param name="registry">Uploads label text to the backend.</param>
    /// <param name="looks">Where nicknames arrive.</param>
    /// <param name="names">Formats a nickname for display.</param>
    public NicknameLabels(LabelRegistry registry, LookChannel looks, NicknameFormatter names)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(names);

        _registry = registry;
        _looks = looks;
        _names = names;
        looks.Changed += this.Refresh;
        this.Refresh();
    }

    /// <summary>Uploads the labels of every nickname now known (call again after the UI language changed).</summary>
    public void Refresh()
    {
        string? own = _looks.TryGetLocal(out LookInfo local) && !string.IsNullOrEmpty(local.Nickname) ? local.Nickname : null;
        foreach (string nickname in _looks.KnownNicknames())
        {
            _byNickname[nickname] = _registry.Intern(_names.Display(nickname), string.Equals(nickname, own, StringComparison.Ordinal) ? OwnStyle : NormalStyle);
        }
    }

    /// <summary>Gets the label id of a nickname. Allocation-free; safe inside the frame.</summary>
    /// <param name="nickname">The nickname as the server sent it; may be null or empty.</param>
    /// <returns>The label id, or zero when none (nothing is drawn).</returns>
    public int IdOf(string? nickname) => nickname is not null && _byNickname.TryGetValue(nickname, out int id) ? id : 0;

    /// <inheritdoc/>
    public void Dispose() => _looks.Changed -= this.Refresh;
}
