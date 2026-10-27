// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Device;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class DeviceGateTests
{
    private const string Windows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private const string MacSafari = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";
    private const string Linux = "Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0";
    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1";
    private const string IPadMobile = "Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1";
    private const string AndroidPhone = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";
    private const string AndroidTablet = "Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public static TheoryData<string, bool?, string, string, bool, bool, int, int, DeviceVerdict> Cases => new()
    {
        // name, hint, UA, platform, coarse, hover, touch points, width, verdict
        { "windows desktop", false, Windows, "Win32", false, true, 0, 1920, DeviceVerdict.Allowed },
        { "windows desktop 1280", false, Windows, "Win32", false, true, 0, 1280, DeviceVerdict.Allowed },
        { "mac safari (no hints)", null, MacSafari, "MacIntel", false, true, 0, 1440, DeviceVerdict.Allowed },
        { "firefox linux (no hints)", null, Linux, "Linux x86_64", false, true, 0, 1366, DeviceVerdict.Allowed },
        { "touch laptop keeps mouse", false, Windows, "Win32", false, true, 10, 1536, DeviceVerdict.Allowed },
        { "touch laptop with coarse primary but hover", false, Windows, "Win32", true, true, 10, 1536, DeviceVerdict.Allowed },
        { "exactly the minimum width", false, Windows, "Win32", false, true, 0, 1024, DeviceVerdict.Allowed },
        { "one pixel under the minimum", false, Windows, "Win32", false, true, 0, 1023, DeviceVerdict.TooNarrow },
        { "small desktop window", false, Windows, "Win32", false, true, 0, 700, DeviceVerdict.TooNarrow },
        { "iphone", null, IPhone, "iPhone", true, false, 5, 390, DeviceVerdict.TouchBlocked },
        { "iphone landscape is still a phone", null, IPhone, "iPhone", true, false, 5, 844, DeviceVerdict.TouchBlocked },
        { "android phone with hints", true, AndroidPhone, "Linux armv81", true, false, 5, 412, DeviceVerdict.TouchBlocked },
        { "hint alone says mobile", true, Windows, "Win32", false, true, 0, 1920, DeviceVerdict.TouchBlocked },
        { "android tablet, hints say not mobile", false, AndroidTablet, "Linux armv81", true, false, 5, 1280, DeviceVerdict.TouchBlocked },
        { "ipad with an iPad user agent", null, IPadMobile, "iPad", true, false, 5, 1024, DeviceVerdict.TouchBlocked },
        { "ipad pretending to be a mac", null, MacSafari, "MacIntel", true, false, 5, 1180, DeviceVerdict.TouchBlocked },
        { "ipad pretending to be a mac, wide", null, MacSafari, "MacIntel", false, true, 5, 1366, DeviceVerdict.TouchBlocked },
        { "unknown touch-only device", null, Linux, "Linux x86_64", true, false, 2, 1280, DeviceVerdict.TouchBlocked },
        { "coarse pointer but no touch points is a desktop", null, Windows, "Win32", true, false, 0, 1920, DeviceVerdict.Allowed },
        { "no signals at all", null, "", "", false, false, 0, 1920, DeviceVerdict.Allowed },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluate_DefaultPolicy_FollowsTheSignals(string name, bool? hint, string userAgent, string platform, bool coarse, bool hover, int touchPoints, int width, DeviceVerdict expected)
    {
        DeviceSignals signals = new(hint, userAgent, platform, coarse, hover, touchPoints, width);

        DeviceVerdict verdict = DeviceGate.Evaluate(signals, DevicePolicy.Default);

        verdict.Should().Be(expected, name);
    }

    [Fact]
    public void Evaluate_BlockSwitchedOff_LetsAPhoneThrough()
    {
        DeviceSignals phone = new(true, IPhone, "iPhone", true, false, 5, 1200);

        DeviceVerdict verdict = DeviceGate.Evaluate(phone, new DevicePolicy(false, 1024));

        verdict.Should().Be(DeviceVerdict.Allowed);
    }

    [Fact]
    public void Evaluate_BlockSwitchedOff_StillRequiresTheMinimumWidth()
    {
        DeviceSignals phone = new(true, IPhone, "iPhone", true, false, 5, 390);

        DeviceVerdict verdict = DeviceGate.Evaluate(phone, new DevicePolicy(false, 1024));

        verdict.Should().Be(DeviceVerdict.TooNarrow);
    }

    [Fact]
    public void Evaluate_CustomMinimumWidth_IsUsed()
    {
        DeviceSignals desktop = new(false, Windows, "Win32", false, true, 0, 1100);

        DeviceGate.Evaluate(desktop, new DevicePolicy(true, 1200)).Should().Be(DeviceVerdict.TooNarrow);
        DeviceGate.Evaluate(desktop, new DevicePolicy(true, 1100)).Should().Be(DeviceVerdict.Allowed);
    }

    [Fact]
    public void Default_Policy_BlocksTouchDevicesAt1024()
    {
        DevicePolicy policy = DevicePolicy.Default;

        policy.BlockTouchDevices.Should().BeTrue();
        policy.MinimumWidth.Should().Be(1024);
    }
}
