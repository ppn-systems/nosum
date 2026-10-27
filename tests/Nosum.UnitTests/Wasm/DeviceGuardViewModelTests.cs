// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class DeviceGuardViewModelTests
{
    private static ResourceManagerStringLocalizerFactory Factory() => new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);

    private static DeviceGuardViewModel Create(int width) => new(
        new StringLocalizer<Language>(Factory()),
        new DeviceSignals(false, "Mozilla/5.0 (Windows NT 10.0)", "Win32", false, true, 0, width),
        DevicePolicy.Default);

    [Fact]
    public void UpdateWidth_CrossingTheMinimum_RaisesChangedOnce()
    {
        DeviceGuardViewModel vm = Create(800);
        int raised = 0;
        vm.Changed += () => raised++;

        vm.UpdateWidth(900);
        vm.UpdateWidth(1200);
        vm.UpdateWidth(1300);

        raised.Should().Be(1);
        vm.Verdict.Should().Be(DeviceVerdict.Allowed);
    }

    [Theory]
    [InlineData("vi", "Nosum hiện chỉ chơi được trên máy tính. Hãy mở trang này trên PC nhé!")]
    [InlineData("en", "Nosum is for computers for now. Please open this page on a PC!")]
    public void BlockedText_ForCulture_ComesFromTheResources(string culture, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            Create(1920).BlockedText.Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void NarrowText_InEnglish_NamesTheMinimumWidth()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            Create(800).NarrowText.Should().Contain("1024");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
