// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.Gameplay.Players;

/// <summary>
/// The blocklist behind <see cref="ProfanityFilter"/>: Vietnamese (written without accents) and English profanity, slurs and
/// the common teencode abbreviations. Data only; every entry is lower-case ASCII and already in the filter's normal form
/// (letters only, no separators, no digits). Add a word to the list that fits it:
/// <list type="bullet">
/// <item><see cref="Substrings"/>: long or unmistakable words, found anywhere inside a nickname with its separators removed. A word
/// belongs here only when no ordinary name or word contains it (check Vietnamese names with the spelling of the words glued
/// together: "Thang Nguyen" contains "thangngu"). Keep the list in step with the false-positive tests.</item>
/// <item><see cref="Tokens"/>: short abbreviations and words that sit inside innocent words ("ass" in "class", "lon" in "Long",
/// "cock" in "Hancock", "dm" in "admin"). They count only when they are a whole word of the nickname, that is when the
/// text between separators (space, underscore) equals the entry, ignoring digits and repeated letters ("dm_99", "DMMM", "vcl2024").</item>
/// </list>
/// Vietnamese words that are also common given names or syllables ("diem", "du", "lon" inside "Long", "chim", "ngu" inside "Nguyen")
/// are either token-only or left out on purpose.
/// </summary>
public static class ProfanityTerms
{
    /// <summary>Terms found anywhere in the nickname (separators removed, leet undone).</summary>
    public static IReadOnlyList<string> Substrings => s_substrings;

    /// <summary>Terms that count only as a whole word of the nickname.</summary>
    public static IReadOnlyList<string> Tokens => s_tokens;

    /// <summary>Ordinary words that contain a substring term (the "Scunthorpe problem"); they are blanked out before the substrings are searched.</summary>
    public static IReadOnlyList<string> Innocent => s_innocent;

    private static readonly string[] s_substrings =
    [
        // -- Vietnamese: "dit me" family and insults
        "ditme", "ditmay", "ditcon", "djtme", "dumay", "dumemay", "conmemay", "concac", "concak", "cailon", "khonnan", "sucvat", "chochet",
        "dcmm", "dkmm", "dmcs", "vkl", "clgt",

        // -- English: profanity and sexual terms
        "fuck", "fck", "shit", "bitch", "bastard", "asshole", "arsehole", "cunt", "pussy", "cocksucker", "dickhead", "whore", "slut",
        "wanker", "bollocks", "dildo", "jizz", "porn", "blowjob", "handjob", "cumshot", "titties",

        // -- English: slurs and hate
        "nigger", "nigga", "faggot", "retard", "tranny", "chinaman", "spastic", "rapist", "hitler", "neonazi", "kkk",
    ];

    private static readonly string[] s_tokens =
    [
        // -- Vietnamese teencode and short words
        "dm", "dmm", "dcm", "dkm", "dmn", "dit", "djt", "dcl", "dcmn", "cmm", "cmn", "vcl", "vl", "vcd", "vlxx", "clm", "cl", "cc",
        "cac", "cak", "cax", "lon", "loz", "buoi", "dume", "duma", "concho",

        // -- English
        "ass", "arse", "dick", "cock", "cum", "tits", "titty", "boob", "boobs", "anal", "penis", "vagina", "sex", "xxx", "fag", "fuk", "fuc",
        "fvck", "twat", "prick", "wank", "cuck", "coon", "spic", "chink", "gook", "kike", "paki", "rape", "nazi", "milf", "orgy", "thot",
    ];

    private static readonly string[] s_innocent =
    [
        "scunthorpe", "shiitake", "shitake", "ashitaka", "sushi", "therapist", "mashita", "hashita", "retardant", "niggardly", "pornichet",
    ];
}
