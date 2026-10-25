// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace Nosum.Wasm.ViewModels.Localization;

/// <summary>
/// <see cref="IStringLocalizer{T}"/> backed directly by <see cref="ResourceManager"/> (the
/// <c>.resx</c> next to <typeparamref name="TResource"/>), registered without <c>AddLocalization</c>:
/// the interpreter this client runs on aborts inside that helper's factory wiring, and no
/// extra package is needed.
/// </summary>
/// <typeparam name="TResource">Marker type whose full name is the resource base name.</typeparam>
public sealed class ResxStringLocalizer<TResource> : IStringLocalizer<TResource>
{
    private static readonly ResourceManager s_resources = new(typeof(TResource).FullName ?? typeof(TResource).Name, typeof(TResource).Assembly);

    /// <inheritdoc/>
    public LocalizedString this[string name]
    {
        get
        {
            string? value = s_resources.GetString(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
        }
    }

    /// <inheritdoc/>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            string? format = s_resources.GetString(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, format ?? name, arguments), resourceNotFound: format is null);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        ResourceSet? set = s_resources.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: includeParentCultures);
        if (set is null)
        {
            yield break;
        }

        foreach (DictionaryEntry entry in set)
        {
            yield return new LocalizedString((string)entry.Key, entry.Value as string ?? string.Empty);
        }
    }
}
