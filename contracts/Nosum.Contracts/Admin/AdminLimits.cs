// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Admin;

/// <summary>Limits of the admin intents, shared so a tool can validate before it sends.</summary>
public static class AdminLimits
{
    /// <summary>The longest mute, in hours (30 days).</summary>
    public const int MaxMuteHours = 720;

    /// <summary>How many reports one <c>AdminListReports</c> returns.</summary>
    public const int ReportPage = 20;
}
