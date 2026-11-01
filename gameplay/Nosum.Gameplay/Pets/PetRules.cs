// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Text;
using Nosum.Gameplay.Chat;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Pets;

/// <summary>What a farmer may keep as a companion (docs/adr/ADR-075 D3). <see cref="None"/> is "no pet" on the wire.</summary>
public enum PetKind : byte
{
    /// <summary>No pet.</summary>
    None = 0,

    /// <summary>A cat: follows, sits, sleeps by the bed.</summary>
    Cat = 1,

    /// <summary>A dog: follows, sits, sleeps by the bed.</summary>
    Dog = 2,
}

/// <summary>
/// The rules of the pet companion (docs/adr/ADR-075 D3-D4). The farmer adopts one cat or dog and names it; the name goes through the same
/// cleaning and profanity mask as chat. A pet can be petted once per game day for a small mood and energy bonus. Pure: the server decides,
/// the client reads the same numbers to draw the mood.
/// </summary>
public static class PetRules
{
    /// <summary>The longest pet name, in characters (Unicode scalar values).</summary>
    public const int MaxNameLength = 12;

    /// <summary>The highest mood a pet can have.</summary>
    public const int MaxMood = 100;

    /// <summary>The mood of a new pet.</summary>
    public const int StartingMood = 50;

    /// <summary>The mood a pet gains from one petting.</summary>
    public const int PettingMood = 10;

    /// <summary>The energy a farmer gains from petting their pet once a day.</summary>
    public const int PettingEnergy = 5;

    /// <summary>Cleans a pet name the way chat cleans text (NFC, no control characters, collapsed spaces), masks profanity and cuts it to <see cref="MaxNameLength"/>.</summary>
    /// <param name="text">The name as typed; null is empty.</param>
    /// <returns>The name to keep; empty when nothing printable is left.</returns>
    public static string CleanName(string? text)
    {
        string masked = ChatRules.Prepare(text);
        if (masked.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(masked.Length);
        int count = 0;
        foreach (Rune rune in masked.EnumerateRunes())
        {
            if (count == MaxNameLength)
            {
                break;
            }

            _ = builder.Append(rune.ToString());
            count++;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Whether the pet may be petted today: once per game day.</summary>
    /// <param name="lastPettedDay">The game day it was last petted; 0 when never.</param>
    /// <param name="today">The current game day (<c>GameCalendar.DayNumberAt</c>).</param>
    public static bool CanPet(long lastPettedDay, long today) => lastPettedDay < today;

    /// <summary>The mood after one petting, never above <see cref="MaxMood"/>.</summary>
    /// <param name="mood">The mood before.</param>
    public static int Petted(int mood) => Math.Min(MaxMood, Math.Max(0, mood) + PettingMood);

    /// <summary>What a cat costs at the carpenter's counter, in coins (docs/adr/ADR-075 amendment). Animals there cost 800 to 2,000.</summary>
    public const long CatPrice = 600;

    /// <summary>What a dog costs at the carpenter's counter, in coins.</summary>
    public const long DogPrice = 900;

    /// <summary>The share of the price the carpenter gives back when the farmer rehomes a pet: half, rounded down.</summary>
    public const int RehomeRefundPercent = 50;

    /// <summary>The price of a pet kind in coins; zero for no pet and any other code.</summary>
    /// <param name="kind">The kind code.</param>
    public static long PriceOf(byte kind) => kind switch
    {
        (byte)PetKind.Cat => CatPrice,
        (byte)PetKind.Dog => DogPrice,
        _ => 0,
    };

    /// <summary>The coins a farmer gets back for a pet of this kind when rehoming it.</summary>
    /// <param name="kind">The kind code.</param>
    public static long RehomeRefundOf(byte kind) => PriceOf(kind) * RehomeRefundPercent / 100;

    /// <summary>Whether the code names a pet the farmer can adopt.</summary>
    /// <param name="kind">The code the client sent.</param>
    public static bool IsAdoptable(byte kind) => kind is (byte)PetKind.Cat or (byte)PetKind.Dog;

    /// <summary>The chance, in percent, that the first petting of a game day brings back a forage item (docs/adr/ADR-090 D2).</summary>
    public const int ForageChancePercent = 30;

    /// <summary>The forage items a pet can bring back: wild food from the meadow, each one as likely as the others.</summary>
    public static readonly int[] ForageItems = [ItemCatalog.SpiceBerry, ItemCatalog.WildPlum, ItemCatalog.Mushroom];

    /// <summary>
    /// The forage item a pet brings back on the first petting of a game day, or zero for none (docs/adr/ADR-090 D2). A pure function of the
    /// player and the day, so the same petting gives the same answer on any server and a test can check it.
    /// </summary>
    /// <param name="playerId">The farmer who petted the pet.</param>
    /// <param name="today">The game day of the petting.</param>
    /// <returns>An item id from <see cref="ForageItems"/>, or zero.</returns>
    public static int ForageOf(Guid playerId, int today)
    {
        ulong seed = (ulong)BitConverter.ToInt64(playerId.ToByteArray(), 0) ^ ((ulong)(uint)today * 0x9E37_79B9_7F4A_7C15UL);
        ulong roll = Mix(seed);
        if (roll % 100 >= (ulong)ForageChancePercent)
        {
            return 0;
        }

        return ForageItems[(int)(Mix(roll) % (ulong)ForageItems.Length)];
    }

    // SplitMix64 finaliser: spreads a seed over all 64 bits so that neighbouring days and players do not share rolls.
    private static ulong Mix(ulong value)
    {
        ulong z = value + 0x9E37_79B9_7F4A_7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }
}
