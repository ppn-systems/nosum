// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Device;

/// <summary>What the browser says about the device, read once by <c>page-guard.js</c>. Plain data, so the decision is testable.</summary>
/// <param name="MobileHint"><c>navigator.userAgentData.mobile</c> (UA Client Hints), or <see langword="null"/> where the browser has no such API (Firefox, Safari).</param>
/// <param name="UserAgent"><c>navigator.userAgent</c>; empty when unknown.</param>
/// <param name="Platform"><c>navigator.platform</c>; empty when unknown. Only used to tell an iPad that pretends to be a Mac.</param>
/// <param name="PointerCoarse">The <c>(pointer: coarse)</c> media query: the primary input is a finger.</param>
/// <param name="CanHover">The <c>(hover: hover)</c> media query: the primary input can hover (a mouse or trackpad).</param>
/// <param name="MaxTouchPoints"><c>navigator.maxTouchPoints</c>.</param>
/// <param name="ViewportWidth">The window's inner width in CSS pixels.</param>
public readonly record struct DeviceSignals(
    bool? MobileHint,
    string UserAgent,
    string Platform,
    bool PointerCoarse,
    bool CanHover,
    int MaxTouchPoints,
    int ViewportWidth);
