// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Chat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class ChatRulesTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("hello", "hello")]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("a\tb\nc\r\nd", "a b c d")]
    public void Sanitize_Whitespace_IsTrimmedAndCollapsedToSingleSpaces(string? typed, string expected)
    {
        ChatRules.Sanitize(typed).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_ControlCharacters_AreDropped()
    {
        ChatRules.Sanitize(string.Concat("a", C(0), "b", C(0x1B), "c", C(0x7F), "d", C(0x85), "e")).Should().Be("abcd e", "U+0085 (next line) reads as a space, the other controls vanish");
    }

    [Fact]
    public void Sanitize_BidiOverrideZeroWidthAndLineSeparators_AreDroppedOrReadAsSpaces()
    {
        // U+202E would flip the rest of a line; U+200B hides characters; U+2028/2029 break lines; U+FEFF is a byte order mark.
        ChatRules.Sanitize(string.Concat("evil", C(0x202E), "text", C(0x200B), "here", C(0x2028), "ok", C(0x2029), "!", C(0xFEFF))).Should().Be("eviltexthere ok !", "U+2028 and U+2029 read as spaces, the invisible ones vanish");
    }

    [Fact]
    public void Sanitize_UnassignedPrivateUseAndLoneSurrogates_AreDropped()
    {
        ChatRules.Sanitize(string.Concat("a", C(0xE000), "b")).Should().Be("ab");
        ChatRules.Sanitize(string.Concat("a", C(0xD800), "b")).Should().BeEmpty("a lone surrogate cannot be normalised, so nothing is delivered");
    }

    [Fact]
    public void Sanitize_DecomposedVietnamese_IsNormalisedToNfc()
    {
        string decomposed = string.Concat("Tie", C(0x0302), C(0x0301), "ng Vie", C(0x0323), C(0x0302), "t"); // "Tiếng Việt" written with combining marks
        string clean = ChatRules.Sanitize(decomposed);

        clean.Should().Be("Tiếng Việt");
        clean.IsNormalized(System.Text.NormalizationForm.FormC).Should().BeTrue();
        clean.Length.Should().BeLessThan(decomposed.Length);
    }

    [Fact]
    public void Sanitize_Emoji_AreKeptAndCountedAsOneCharacter()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        string line = string.Concat(Enumerable.Repeat(emoji, ChatRules.MaxLength));

        ChatRules.Sanitize(line).Should().Be(line, "160 emoji are 160 characters although each is two UTF-16 units");
        ChatRules.Sanitize(line + emoji).Should().Be(line, "the 161st character is cut");
    }

    [Fact]
    public void Sanitize_LongText_IsCutAtTheLimitWithoutTrailingSpace()
    {
        string text = string.Join(' ', Enumerable.Repeat("abcdefghi", 40));

        string clean = ChatRules.Sanitize(text);

        clean.Length.Should().BeLessThanOrEqualTo(ChatRules.MaxLength);
        clean.Should().NotEndWith(" ");
        text.Should().StartWith(clean);
        clean.Length.Should().BeGreaterThan(ChatRules.MaxLength - 10, "the limit is used, not undershot");
    }

    [Fact]
    public void Sanitize_TextExactlyAtTheLimit_IsKeptWhole()
    {
        string text = new('x', ChatRules.MaxLength);

        ChatRules.Sanitize(text).Should().Be(text);
        ChatRules.Sanitize(text + "x").Should().HaveLength(ChatRules.MaxLength);
    }

    [Fact]
    public void Prepare_CleansThenMasks()
    {
        ChatRules.Prepare(string.Concat("  hi", C(0x202E), "   you fuck  ")).Should().Be("hi you ****");
        ChatRules.Prepare(string.Concat(C(0), C(0x200B))).Should().BeEmpty();
    }

    [Fact]
    public void Prepare_MarkupIsLeftAloneBecauseTheClientRendersPlainText()
    {
        // Nothing is escaped on the server: the client never interprets markup, so "<b>" is just five characters.
        ChatRules.Prepare("<b>hi</b> & <script>").Should().Be("<b>hi</b> & <script>");
    }

    private static string C(int codePoint) => ((char)codePoint).ToString();
}
