// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Account.C2S;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Client packets retired from the protocol: their classes and opcodes stay reserved and are never reused, but the server has no handler
/// for them. Wire and handler audits skip exactly these.
/// </summary>
internal static class RetiredPackets
{
    /// <summary>The retired account recovery packets (docs/adr/ADR-032 D4, removed).</summary>
    public static readonly Type[] Types = [typeof(RequestPasswordReset), typeof(ConfirmPasswordReset)];
}
