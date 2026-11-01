// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class EmoteAndPetTextTests
{
    private static readonly string[] s_keys =
    [
        "Toolbar_Pet", "Emote_Open", "Emote_Wheel", "Emote_0", "Emote_11", "Emote_Cooldown", "Emote_NotOnMap", "Emote_Invalid",
        "Pet_Cat", "Pet_Dog", "Pet_NoPetTitle", "Pet_BuyHint", "Pet_NameLabel", "Pet_Mood", "Pet_Pet", "Pet_AlreadyPetted", "Pet_Rehome",
        "Pet_RehomeTitle", "Pet_RehomeMessage", "Pet_RehomeConfirm", "Pet_RehomeCancel", "Pet_Status_Ok", "Pet_Status_NoPet",
        "Pet_Status_AlreadyHasPet", "Pet_Status_InvalidKind", "Pet_Status_InvalidName", "Pet_Status_AlreadyPetted", "Pet_Status_Unavailable",
        "Pet_Status_NotAtShop", "Pet_Status_NotEnoughCoins", "Carpenter_Pets", "Carpenter_PetHint", "Carpenter_PetPrice", "Carpenter_BuyPet",
        "Carpenter_PetHave",
    ];

    [Fact]
    public void Resources_EveryEmoteAndPetKey_ExistsInBothLanguages()
    {
        HashSet<string> vietnamese = WasmTestSupport.Keys("Language.resx");
        HashSet<string> english = WasmTestSupport.Keys("Language.en.resx");

        foreach (string key in s_keys)
        {
            vietnamese.Should().Contain(key);
            english.Should().Contain(key);
        }
    }
}
