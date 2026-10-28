// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Chat;
using Nosum.Gameplay.Players;

namespace Nosum.Wasm.ViewModels.Chat;

/// <summary>What a line typed into the chat box asks for.</summary>
public enum ChatCommandKind
{
    /// <summary>Say the text on <see cref="ChatCommand.Channel"/>.</summary>
    Say = 0,

    /// <summary>Whisper the text to <see cref="ChatCommand.Target"/>.</summary>
    Whisper = 1,

    /// <summary>Make <see cref="ChatCommand.Channel"/> the channel for what is typed next.</summary>
    Switch = 2,

    /// <summary>Block <see cref="ChatCommand.Target"/>.</summary>
    Block = 3,

    /// <summary>Unblock <see cref="ChatCommand.Target"/>.</summary>
    Unblock = 4,

    /// <summary>Report <see cref="ChatCommand.Target"/>.</summary>
    Report = 5,

    /// <summary>A whisper or moderation command without its nickname or text.</summary>
    Usage = 6,

    /// <summary>A slash command this client does not know.</summary>
    Unknown = 7,

    /// <summary>Nothing to do (an empty line).</summary>
    None = 8,
}

/// <summary>A typed chat line, parsed. The server still cleans and validates everything; this only decides which request to send.</summary>
/// <param name="Kind">What the line asks for.</param>
/// <param name="Channel">The channel to send on or switch to.</param>
/// <param name="Target">The nickname a whisper or moderation command names, as the server stores it.</param>
/// <param name="Text">The text to send.</param>
public readonly record struct ChatCommand(ChatCommandKind Kind, ChatChannel Channel, string Target, string Text)
{
    /// <summary>
    /// Parses a line typed into the chat box. A plain line is said on <paramref name="current"/>. Commands: <c>/w nick text</c> or <c>/whisper nick text</c>;
    /// <c>/l</c>, <c>/m</c>, <c>/c</c> switch to local, map or co-op chat, or say the rest of the line there once; <c>/block</c>, <c>/unblock</c> and
    /// <c>/report</c> take a nickname. A guest's shown name ("Khách 1234", "Guest 1234") is read as the stored <c>guest-1234</c>.
    /// </summary>
    /// <param name="input">What was typed.</param>
    /// <param name="current">The channel plain lines go to.</param>
    /// <returns>The parsed command.</returns>
    public static ChatCommand Parse(string? input, ChatChannel current)
    {
        string line = (input ?? string.Empty).Trim();
        if (line.Length == 0)
        {
            return new ChatCommand(ChatCommandKind.None, current, string.Empty, string.Empty);
        }

        if (line[0] != '/')
        {
            return new ChatCommand(ChatCommandKind.Say, current, string.Empty, line);
        }

        int space = line.IndexOf(' ', StringComparison.Ordinal);
        string name = (space < 0 ? line[1..] : line[1..space]).ToLowerInvariant();
        string rest = space < 0 ? string.Empty : line[(space + 1)..].TrimStart();

        switch (name)
        {
            case "w":
            case "whisper":
            {
                (string target, string text) = SplitNickname(rest);
                return target.Length == 0 || text.Length == 0
                    ? new ChatCommand(ChatCommandKind.Usage, ChatChannel.Whisper, target, text)
                    : new ChatCommand(ChatCommandKind.Whisper, ChatChannel.Whisper, target, text);
            }

            case "l":
            case "local":
                return ForChannel(ChatChannel.Local, rest);
            case "m":
            case "map":
                return ForChannel(ChatChannel.Map, rest);
            case "c":
            case "coop":
                return ForChannel(ChatChannel.Coop, rest);
            case "block":
                return Moderation(ChatCommandKind.Block, rest, current);
            case "unblock":
                return Moderation(ChatCommandKind.Unblock, rest, current);
            case "report":
                return Moderation(ChatCommandKind.Report, rest, current);
            default:
                return new ChatCommand(ChatCommandKind.Unknown, current, string.Empty, line);
        }
    }

    private static ChatCommand ForChannel(ChatChannel channel, string rest)
        => rest.Length == 0
            ? new ChatCommand(ChatCommandKind.Switch, channel, string.Empty, string.Empty)
            : new ChatCommand(ChatCommandKind.Say, channel, string.Empty, rest);

    private static ChatCommand Moderation(ChatCommandKind kind, string rest, ChatChannel current)
    {
        (string target, string extra) = SplitNickname(rest);
        if (target.Length > 0 && extra.Length == 0)
        {
            return new ChatCommand(kind, current, target, string.Empty);
        }

        if (target.Length > 0 && extra.Length > 0 && NicknameRules.Validate(rest) == NicknameVerdict.Ok)
        {
            return new ChatCommand(kind, current, NicknameRules.Normalize(rest), string.Empty);
        }

        return new ChatCommand(ChatCommandKind.Usage, current, target, extra);
    }

    // The first word (or a quoted string) is the nickname; a guest is shown as two words ("Guest 1234"), which are the one stored name guest-1234.
    private static (string Target, string Text) SplitNickname(string rest)
    {
        if (rest.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (rest[0] == '"')
        {
            int close = rest.IndexOf('"', 1);
            if (close > 1)
            {
                string quoted = rest[1..close].Trim();
                string afterQuote = rest[(close + 1)..].Trim();
                string[] guestWords = quoted.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (guestWords.Length == 2 && IsGuestWord(guestWords[0]) && IsDigits(guestWords[1]))
                {
                    quoted = NicknameRules.GuestPrefix + guestWords[1];
                }

                return (quoted, afterQuote);
            }
        }

        string[] words = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && IsGuestWord(words[0]) && IsDigits(words[1]))
        {
            return (NicknameRules.GuestPrefix + words[1], words.Length > 2 ? words[2].Trim() : string.Empty);
        }

        string text = rest[words[0].Length..].Trim();
        return (words[0], text);
    }

    private static bool IsGuestWord(string word)
        => word.Equals("khách", StringComparison.OrdinalIgnoreCase) || word.Equals("khach", StringComparison.OrdinalIgnoreCase) || word.Equals("guest", StringComparison.OrdinalIgnoreCase);

    private static bool IsDigits(string word) => word.Length > 0 && word.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;
}
