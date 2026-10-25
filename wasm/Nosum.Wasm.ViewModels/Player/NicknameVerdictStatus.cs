// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Account;
using Nosum.Gameplay.Players;

namespace Nosum.Wasm.ViewModels.Player;

/// <summary>Maps the shared nickname rules' verdict to the status code the server would answer with, so the early client feedback reads the same texts.</summary>
internal static class NicknameVerdictStatus
{
    /// <summary>Converts a verdict.</summary>
    /// <param name="verdict">The verdict of <see cref="NicknameRules.Validate"/>.</param>
    /// <returns>The status, or <see langword="null"/> when the nickname is fine.</returns>
    public static AccountStatus? From(NicknameVerdict verdict) => verdict switch
    {
        NicknameVerdict.Ok => null,
        NicknameVerdict.Reserved => AccountStatus.NicknameReserved,
        NicknameVerdict.Profane => AccountStatus.NicknameProfane,
        _ => AccountStatus.InvalidNickname,
    };
}
