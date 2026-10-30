// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>Why a key could not be bound.</summary>
public enum RebindResult
{
    /// <summary>The key is bound; it was free.</summary>
    Bound,

    /// <summary>The key is bound; another action had it and now has the key this action had.</summary>
    Swapped,

    /// <summary>The key is reserved (Escape, Enter, Tab, the arrows, Space and the number keys always keep their job) and nothing changed.</summary>
    Reserved,
}

/// <summary>
/// The key bound to each <see cref="GameAction"/>, as browser key codes (<c>KeyE</c>, <c>KeyW</c>): immutable. One key never serves two actions: binding a key that another
/// action holds swaps the two. The arrows, Space, the number keys, Escape, Enter and Tab are fixed and cannot be bound, so a player can never lose the way out of a panel.
/// </summary>
public sealed class KeyBindings
{
    private static readonly FrozenDictionary<GameAction, string> s_defaults = new Dictionary<GameAction, string>
    {
        [GameAction.MoveUp] = "KeyW",
        [GameAction.MoveDown] = "KeyS",
        [GameAction.MoveLeft] = "KeyA",
        [GameAction.MoveRight] = "KeyD",
        [GameAction.Use] = "KeyE",
        [GameAction.Inventory] = "KeyI",
        [GameAction.Wardrobe] = "KeyC",
        [GameAction.Trade] = "KeyT",
        [GameAction.Coop] = "KeyG",
        [GameAction.Profile] = "KeyN",
        [GameAction.Help] = "KeyH",
        [GameAction.Rotate] = "KeyR",
        [GameAction.Grab] = "KeyF",
        [GameAction.PickUp] = "KeyQ",
        [GameAction.Minimap] = "KeyM",
        [GameAction.WorldMap] = "Tab",
        [GameAction.Journal] = "KeyJ",
    }.ToFrozenDictionary();

    private readonly Dictionary<GameAction, string> _codes;

    private KeyBindings(Dictionary<GameAction, string> codes) => _codes = codes;

    /// <summary>Gets the default bindings (W A S D, E, I M C T G N H R F Q).</summary>
    public static KeyBindings Default { get; } = new(new Dictionary<GameAction, string>(s_defaults));

    /// <summary>Gets the key code bound to an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The browser key code, for example <c>KeyE</c>.</returns>
    public string CodeOf(GameAction action) => _codes[action];

    /// <summary>Gets the key code an action has by default.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The browser key code.</returns>
    public static string DefaultOf(GameAction action) => s_defaults[action];

    /// <summary>Gets whether a key can never be bound.</summary>
    /// <param name="code">The browser key code.</param>
    /// <returns><see langword="true"/> for anything that is not a letter or a punctuation key: Escape, Enter, Tab, the arrows, Space, the number keys, function keys, the fixed combat skill hotkeys (<c>KeyZ</c>, <c>KeyX</c>, <c>KeyV</c>), and the emote wheel's hotkey (<c>KeyB</c>, docs/adr/ADR-075 D1).</returns>
    public static bool IsReserved(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code is "KeyZ" or "KeyX" or "KeyV" or "KeyB")
        {
            return true;
        }

        return !(code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4
            || code is "Comma" or "Period" or "Slash" or "Semicolon" or "Quote" or "BracketLeft" or "BracketRight" or "Backslash" or "Minus" or "Equal" or "Backquote");
    }

    /// <summary>Binds a key to an action.</summary>
    /// <param name="action">The action.</param>
    /// <param name="code">The browser key code pressed.</param>
    /// <param name="result">The new bindings; this instance when the key is reserved or already the action's.</param>
    /// <returns>What happened.</returns>
    public RebindResult TryRebind(GameAction action, string code, out KeyBindings result)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (IsReserved(code))
        {
            result = this;
            return RebindResult.Reserved;
        }

        if (string.Equals(_codes[action], code, StringComparison.Ordinal))
        {
            result = this;
            return RebindResult.Bound;
        }

        Dictionary<GameAction, string> next = new(_codes);
        RebindResult outcome = RebindResult.Bound;
        foreach ((GameAction other, string held) in _codes)
        {
            if (other != action && string.Equals(held, code, StringComparison.Ordinal))
            {
                next[other] = _codes[action];
                outcome = RebindResult.Swapped;
                break;
            }
        }

        next[action] = code;
        result = new KeyBindings(next);
        return outcome;
    }

    /// <summary>Writes the bindings as <c>Action=Code;Action=Code</c>, the form stored in the browser and read by <c>renderer.js</c>.</summary>
    /// <returns>The text; only the actions that differ from the default are listed, so a new default key reaches players who never changed it.</returns>
    public string Serialize()
    {
        StringBuilder text = new();
        foreach (GameAction action in Enum.GetValues<GameAction>())
        {
            if (!string.Equals(_codes[action], s_defaults[action], StringComparison.Ordinal))
            {
                text.Append(text.Length > 0 ? ";" : string.Empty).Append(action.ToString()).Append('=').Append(_codes[action]);
            }
        }

        return text.ToString();
    }

    /// <summary>Writes every binding (not only the changed ones), for the page script.</summary>
    /// <returns>The text, <c>Action=Code;...</c> for all actions.</returns>
    public string SerializeAll()
    {
        StringBuilder text = new();
        foreach (GameAction action in Enum.GetValues<GameAction>())
        {
            text.Append(text.Length > 0 ? ";" : string.Empty).Append(action.ToString()).Append('=').Append(_codes[action]);
        }

        return text.ToString();
    }

    /// <summary>Reads bindings written by <see cref="Serialize"/>; anything unknown, reserved or twice-bound is ignored, so a damaged value falls back to the defaults.</summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <returns>The bindings.</returns>
    public static KeyBindings Parse(string? text)
    {
        KeyBindings result = Default;
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (string pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = pair.IndexOf('=', StringComparison.Ordinal);
            if (at > 0 && Enum.TryParse(pair[..at], out GameAction action) && Enum.IsDefined(action))
            {
                _ = result.TryRebind(action, pair[(at + 1)..], out result);
            }
        }

        return result;
    }

    /// <summary>Returns the name printed on a key cap.</summary>
    /// <param name="code">The browser key code.</param>
    /// <returns>For example <c>E</c> for <c>KeyE</c>, <c>,</c> for <c>Comma</c>.</returns>
    public static string CapOf(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code switch
        {
            _ when code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4 => code[3].ToString(),
            "Comma" => ",",
            "Period" => ".",
            "Slash" => "/",
            "Semicolon" => ";",
            "Quote" => "'",
            "BracketLeft" => "[",
            "BracketRight" => "]",
            "Backslash" => "\\",
            "Minus" => "-",
            "Equal" => "=",
            "Backquote" => "`",
            "Space" => "Space",
            "Escape" => "Esc",
            "Enter" or "NumpadEnter" => "Enter",
            "ArrowUp" => "Up",
            "ArrowDown" => "Down",
            "ArrowLeft" => "Left",
            "ArrowRight" => "Right",
            _ when code.StartsWith("Digit", StringComparison.Ordinal) && code.Length == 6 => code[5].ToString(),
            _ when code.StartsWith("Numpad", StringComparison.Ordinal) => "Num " + code[6..],
            _ => code,
        };
    }
}
