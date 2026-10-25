// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Rendering;

/// <summary>
/// Gives every distinct piece of text (a nickname) an integer id and uploads it to the backend once, so a frame only records
/// <see cref="RenderCommandBuffer.DrawLabel"/> with the id: no string, no allocation and no interop beyond the frame's one crossing.
/// The backend renders the text into a cached bitmap when it receives it (glyph caching). Call <see cref="Intern"/> outside the frame
/// (when a name arrives) and <see cref="TryGet"/> inside it.
/// </summary>
public sealed class LabelRegistry
{
    private readonly Action<int, string, int> _upload;
    private readonly Dictionary<(string Text, int Style), int> _ids = [];
    private readonly int _capacity;
    private int _next = 1;

    /// <summary>Initializes a registry.</summary>
    /// <param name="upload">Sends a label to the backend: id, text, style.</param>
    /// <param name="capacity">Most labels kept; later texts are not drawn.</param>
    public LabelRegistry(Action<int, string, int> upload, int capacity = 512)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _upload = upload;
        _capacity = capacity;
    }

    /// <summary>Gets the number of labels uploaded.</summary>
    public int Count => _ids.Count;

    /// <summary>Returns the id of a label, uploading it the first time. Not for use inside the frame.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="style">The backend's style number (0 normal, 1 the local player's).</param>
    /// <returns>The label id, or zero for empty text or when the registry is full.</returns>
    public int Intern(string text, int style)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return 0;
        }

        if (_ids.TryGetValue((text, style), out int known))
        {
            return known;
        }

        if (_ids.Count >= _capacity)
        {
            return 0;
        }

        int id = _next++;
        _ids[(text, style)] = id;
        _upload(id, text, style);
        return id;
    }

    /// <summary>Looks up a label that was interned earlier. Safe inside the frame: no allocation, no upload.</summary>
    /// <param name="text">The text.</param>
    /// <param name="style">The style.</param>
    /// <param name="id">The id when found.</param>
    /// <returns><see langword="true"/> when the label exists.</returns>
    public bool TryGet(string text, int style, out int id) => _ids.TryGetValue((text, style), out id);
}
