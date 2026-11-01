// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
public sealed class SettingsTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public string? Text { get; set; }

        public string? Read() => this.Text;

        public void Write(string text) => this.Text = text;
    }

    private static StringLocalizer<Language> Text()
        => new(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void Parse_NothingStored_GivesDefaults()
    {
        ClientSettings.Parse(null).Should().Be(ClientSettings.Default);
        ClientSettings.Parse(string.Empty).Should().Be(ClientSettings.Default);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        _ = KeyBindings.Default.TryRebind(GameAction.Use, "KeyJ", out KeyBindings keys);
        ClientSettings settings = new()
        {
            Language = GameLanguage.English,
            MusicVolume = 10,
            SoundVolume = 90,
            Muted = true,
            UiScale = 4,
            ShowNames = false,
            ReduceMotion = true,
            HighContrast = true,
            Bindings = keys,
            TutorialEnabled = false,
            TutorialDone = 5,
        };

        ClientSettings back = ClientSettings.Parse(settings.Serialize());

        back.Should().BeEquivalentTo(settings, o => o.Excluding(s => s.Bindings));
        back.Bindings.CodeOf(GameAction.Use).Should().Be("KeyJ");
        back.Bindings.CodeOf(GameAction.Inventory).Should().Be("KeyI");
    }

    [Fact]
    public void Parse_DamagedValues_KeepDefaults()
    {
        ClientSettings back = ClientSettings.Parse("music=loud\nscale=9\nlang=99\nsound=500\nkeys=Use=Escape;Nope=KeyZ\ngarbage\n");

        back.MusicVolume.Should().Be(70);
        back.UiScale.Should().Be(0);
        back.Language.Should().Be(GameLanguage.Vietnamese);
        back.SoundVolume.Should().Be(100);
        back.Bindings.CodeOf(GameAction.Use).Should().Be("KeyE");
    }

    [Fact]
    public void Rebind_KeyOfAnotherAction_SwapsThem()
    {
        RebindResult result = KeyBindings.Default.TryRebind(GameAction.Use, "KeyI", out KeyBindings keys);

        result.Should().Be(RebindResult.Swapped);
        keys.CodeOf(GameAction.Use).Should().Be("KeyI");
        keys.CodeOf(GameAction.Inventory).Should().Be("KeyE");
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("Space")]
    [InlineData("Digit3")]
    [InlineData("ArrowUp")]
    [InlineData("Enter")]
    [InlineData("Tab")]
    [InlineData("KeyZ")]
    [InlineData("KeyX")]
    [InlineData("KeyV")]
    public void Rebind_ReservedKey_ChangesNothing(string code)
    {
        KeyBindings.Default.TryRebind(GameAction.Use, code, out KeyBindings keys).Should().Be(RebindResult.Reserved);

        keys.Should().BeSameAs(KeyBindings.Default);
    }

    [Fact]
    public void Bindings_NoKeyServesTwoActions()
    {
        KeyBindings keys = KeyBindings.Default;
        foreach (GameAction action in Enum.GetValues<GameAction>())
        {
            _ = keys.TryRebind(action, "KeyJ", out keys);
        }

        Enum.GetValues<GameAction>().Select(keys.CodeOf).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Settings_ChangeIsStoredAndAnnounced()
    {
        MemoryStore store = new();
        using SettingsViewModel vm = new(Text(), store);
        List<ClientSettings> seen = [];
        vm.Applied += seen.Add;

        vm.SetUiScale(3);
        vm.SetMusicVolume(150);
        vm.SetHighContrast(true);

        seen.Should().HaveCount(3);
        vm.Current.MusicVolume.Should().Be(100);
        ClientSettings.Parse(store.Text).Should().BeEquivalentTo(vm.Current, o => o.Excluding(s => s.Bindings));
    }

    [Fact]
    public void Settings_StoredValuesAreLoaded()
    {
        MemoryStore store = new() { Text = new ClientSettings { UiScale = 5, ReduceMotion = true }.Serialize() };
        using SettingsViewModel vm = new(Text(), store);

        vm.Current.UiScale.Should().Be(5);
        vm.Current.ReduceMotion.Should().BeTrue();
    }

    [Fact]
    public void Capture_BindsKeyToTheActionListenedFor()
    {
        using SettingsViewModel vm = new(Text(), new MemoryStore());

        vm.BeginRebind(GameAction.Rotate);
        vm.Capture("Digit1").Should().Be(RebindResult.Reserved);
        vm.Capture("KeyX").Should().Be(RebindResult.Reserved);
        vm.Listening.Should().Be(GameAction.Rotate);
        vm.Capture("KeyK").Should().Be(RebindResult.Bound);

        vm.Listening.Should().BeNull();
        vm.CapOf(GameAction.Rotate).Should().Be("K");
    }

    // A clock whose timers fire when the test moves it on, so the hint timing is exact and needs no real wait.
    private sealed class ScheduledTime : TimeProvider
    {
        private readonly List<ScheduledTimer> _timers = [];
        private TimeSpan _now;

        // The clock's timestamps move with the test's time too, so the view model's idle count (TimeProvider.GetElapsedTime) reads the same moment.
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now.Ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ScheduledTimer timer = new(this, callback, state, _now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (ScheduledTimer timer in _timers.ToArray())
            {
                if (timer.Due is { } due && due <= _now)
                {
                    timer.Due = null;
                    timer.Fire();
                }
            }
        }

        public void Forget(ScheduledTimer timer) => _timers.Remove(timer);

        public sealed class ScheduledTimer : ITimer
        {
            private readonly ScheduledTime _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public ScheduledTimer(ScheduledTime owner, TimerCallback callback, object? state, TimeSpan due)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                this.Due = due;
            }

            public TimeSpan? Due { get; set; }

            public void Fire() => _callback(_state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                this.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.Now + dueTime;
                return true;
            }

            public void Dispose() => _owner.Forget(this);

            public ValueTask DisposeAsync()
            {
                this.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private TimeSpan Now => _now;
    }

    [Fact]
    public void Hints_WaitForAnIdleMoment_AndFadeAfterTheirTime()
    {
        ScheduledTime clock = new();
        using SettingsViewModel settings = new(Text(), new MemoryStore());
        using OnboardingViewModel hints = new(settings, Text(), clock);
        hints.Start();

        hints.IsVisible.Should().BeFalse("the first hint waits for the player to be idle");
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds - 1));
        hints.IsVisible.Should().BeFalse();

        hints.NoteActivity();
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds - 1));
        hints.IsVisible.Should().BeFalse("input during the wait starts the idle count again");

        clock.Advance(TimeSpan.FromSeconds(1));
        hints.IsVisible.Should().BeTrue();
        hints.Hint.Should().Contain("W");

        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.HintSeconds));
        hints.IsVisible.Should().BeFalse("the hint fades after its time");
    }

    [Fact]
    public void Hints_NextHintComesAfterTheStepIsDoneAndTheIdleMoment()
    {
        ScheduledTime clock = new();
        using SettingsViewModel settings = new(Text(), new MemoryStore());
        using OnboardingViewModel hints = new(settings, Text(), clock);
        hints.Start();
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds));
        hints.IsVisible.Should().BeTrue();

        // The player is walking: the step is not done yet, so nothing new comes up.
        hints.Signal(TutorialStep.Use);
        hints.IsVisible.Should().BeTrue("the walk hint is still the current one");

        // The walk is done: the next hint waits for the player to stop giving input.
        hints.Signal(TutorialStep.Move);
        hints.IsVisible.Should().BeFalse("the next hint is not shown while the player is still acting");
        hints.NoteActivity();
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds));
        hints.IsVisible.Should().BeTrue();
        hints.Current.Should().Be(TutorialStep.Town);
    }

    [Fact]
    public void Hints_FollowTheStepsInOrderAndAreShownOnce()
    {
        MemoryStore store = new();
        ScheduledTime clock = new();
        using SettingsViewModel settings = new(Text(), store);
        using OnboardingViewModel hints = new(settings, Text(), clock);
        hints.Start();
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds));

        hints.Current.Should().Be(TutorialStep.Move);
        hints.IsVisible.Should().BeTrue();
        hints.Hint.Should().Contain("W").And.NotContain("Hint_");

        hints.Signal(TutorialStep.Use);
        hints.Current.Should().Be(TutorialStep.Move, "a step done early does not skip the one before it");
        hints.Signal(TutorialStep.Move);
        hints.Current.Should().Be(TutorialStep.Town, "the Sell and Shop hints are steps of the quest chain now (docs/adr/ADR-036 D4)");

        hints.Expire();
        hints.IsVisible.Should().BeFalse();

        using SettingsViewModel again = new(Text(), store);
        using OnboardingViewModel next = new(again, Text(), clock);
        next.Start();
        next.Current.Should().Be(TutorialStep.Town, "the finished steps are remembered on this browser");
    }

    [Fact]
    public void Hints_TurnedOffOrFinished_ShowNothing()
    {
        ScheduledTime clock = new();
        using SettingsViewModel settings = new(Text(), new MemoryStore());
        using OnboardingViewModel hints = new(settings, Text(), clock);
        hints.Start();

        settings.SetTutorialEnabled(false);
        hints.IsVisible.Should().BeFalse();

        settings.RestartTutorial();
        clock.Advance(TimeSpan.FromSeconds(OnboardingViewModel.IdleSeconds));
        hints.IsVisible.Should().BeTrue();
        foreach (TutorialStep step in Enum.GetValues<TutorialStep>())
        {
            hints.Signal(step);
        }

        hints.Current.Should().BeNull();
        hints.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Parse_PanelScaleOutOfRange_KeepsDefault()
    {
        ClientSettings.Parse("panel=60").PanelScale.Should().Be(100);
        ClientSettings.Parse("panel=160").PanelScale.Should().Be(100);
        ClientSettings.Parse("panel=125").PanelScale.Should().Be(125);
    }

    [Fact]
    public void Serialize_AutoMotion_IsStoredAsAuto()
    {
        ClientSettings settings = new() { ReduceMotion = null };

        settings.Serialize().Should().Contain("calm=auto\n");
        ClientSettings.Parse(settings.Serialize()).ReduceMotion.Should().BeNull();
        ClientSettings.Parse("calm=0").ReduceMotion.Should().BeFalse();
    }

    [Fact]
    public void ReducesMotion_NoChoice_FollowsTheSystem()
    {
        ClientSettings.Default.ReducesMotion(systemPrefers: true).Should().BeTrue();
        ClientSettings.Default.ReducesMotion(systemPrefers: false).Should().BeFalse();
    }

    [Fact]
    public void ReducesMotion_ChoiceOverridesTheSystem()
    {
        new ClientSettings { ReduceMotion = false }.ReducesMotion(systemPrefers: true).Should().BeFalse();
        new ClientSettings { ReduceMotion = true }.ReducesMotion(systemPrefers: false).Should().BeTrue();
    }

    [Fact]
    public void SetSystemReducesMotion_NoChoice_ChangesTheEffectiveValue()
    {
        using SettingsViewModel vm = new(Text(), new MemoryStore());
        List<ClientSettings> seen = [];
        vm.Applied += seen.Add;

        vm.SetSystemReducesMotion(true);

        vm.ReducesMotion.Should().BeTrue();
        seen.Should().ContainSingle();
        vm.Current.ReduceMotion.Should().BeNull("the player has not chosen yet");
    }

    [Fact]
    public void SetSystemReducesMotion_SameValueAgain_AnnouncesNothing()
    {
        using SettingsViewModel vm = new(Text(), new MemoryStore());
        vm.SetSystemReducesMotion(true);
        List<ClientSettings> seen = [];
        vm.Applied += seen.Add;

        vm.SetSystemReducesMotion(true);

        seen.Should().BeEmpty();
    }

    [Fact]
    public void SetReduceMotion_OverridesTheSystemAndIsStored()
    {
        MemoryStore store = new();
        using SettingsViewModel vm = new(Text(), store);
        vm.SetSystemReducesMotion(true);

        vm.SetReduceMotion(false);

        vm.ReducesMotion.Should().BeFalse();
        ClientSettings.Parse(store.Text).ReduceMotion.Should().BeFalse();
    }

    [Fact]
    public void FollowSystemMotion_ClearsTheChoice()
    {
        using SettingsViewModel vm = new(Text(), new MemoryStore());
        vm.SetReduceMotion(false);
        vm.SetSystemReducesMotion(true);

        vm.FollowSystemMotion();

        vm.Current.ReduceMotion.Should().BeNull();
        vm.ReducesMotion.Should().BeTrue();
    }

    [Fact]
    public void SetPanelScale_OutsideTheRange_ClampsToIt()
    {
        using SettingsViewModel vm = new(Text(), new MemoryStore());

        vm.SetPanelScale(40);
        vm.Current.PanelScale.Should().Be(ClientSettings.MinPanelScale);

        vm.SetPanelScale(400);
        vm.Current.PanelScale.Should().Be(ClientSettings.MaxPanelScale);

        vm.SetPanelScale(125);
        vm.Current.PanelScale.Should().Be(125);
    }

    [Fact]
    public void Text_EveryNewKeyExistsInBothLanguages()
    {
        string root = FindRoot();
        string[] files = [Path.Combine(root, "wasm/Nosum.Wasm.ViewModels/Resources/Language.resx"), Path.Combine(root, "wasm/Nosum.Wasm.ViewModels/Resources/Language.en.resx")];
        string[] needed = Enum.GetValues<GameAction>().Select(a => "Action_" + a)
            .Concat(Enum.GetValues<TutorialStep>().Select(s => "Hint_" + s))
            .Concat(["Settings_TabGeneral", "Settings_TabDisplay", "Settings_TabSound", "Settings_TabControls", "Settings_TabCredits", "Settings_KeySwapped", "Settings_KeyReserved", "Touch_Use", "Touch_UseLabel", "Touch_Stick", "Settings_PanelScale", "Settings_PanelScaleLabel", "Settings_MotionAuto"]).ToArray();

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            needed.Where(k => !text.Contains($"name=\"{k}\"", StringComparison.Ordinal)).Should().BeEmpty(file);
        }
    }

    [Fact]
    public void RendererScript_KnowsEveryBindableAction()
    {
        string script = File.ReadAllText(Path.Combine(FindRoot(), "engine/Nosum.Rendering.Web/wwwroot/renderer.js"));

        foreach (GameAction action in Enum.GetValues<GameAction>())
        {
            script.Should().Contain(action + ": {", $"renderer.js must map {action} to an input bit");
            script.Should().Contain(action + "=" + KeyBindings.DefaultOf(action), "its default key must match the script's");
        }
    }

    [Fact]
    public void Theme_TextColoursReachContrast4Point5OnParchment()
    {
        string css = File.ReadAllText(Path.Combine(FindRoot(), "wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.css"));
        string rootBlock = css[css.IndexOf(":root {", StringComparison.Ordinal)..css.IndexOf("html,", StringComparison.Ordinal)];
        string[] backgrounds = ["--nosum-parchment", "--nosum-parchment-deep"];
        string[] texts = ["--nosum-ink", "--nosum-ink-soft", "--nosum-ink-faint", "--nosum-heading", "--nosum-danger", "--nosum-ok"];

        foreach (string background in backgrounds)
        {
            foreach (string text in texts)
            {
                Ratio(Colour(rootBlock, text), Colour(rootBlock, background)).Should().BeGreaterThanOrEqualTo(4.5, $"{text} on {background}");
            }
        }
    }

    [Fact]
    public void Theme_HighContrastInkIsDarkerStill()
    {
        string css = File.ReadAllText(Path.Combine(FindRoot(), "wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.css"));
        string high = css[css.IndexOf(":root[data-contrast=\"high\"] {", StringComparison.Ordinal)..];
        string normal = css[..css.IndexOf("html,", StringComparison.Ordinal)];

        Ratio(Colour(high, "--nosum-ink-faint"), Colour(normal, "--nosum-parchment-deep")).Should().BeGreaterThan(Ratio(Colour(normal, "--nosum-ink-faint"), Colour(normal, "--nosum-parchment-deep")));
        Ratio(Colour(high, "--nosum-ink"), Colour(normal, "--nosum-parchment")).Should().BeGreaterThanOrEqualTo(7);
    }

    private static string FindRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AGENTS.md")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (double R, double G, double B) Colour(string css, string name)
    {
        Match m = Regex.Match(css, Regex.Escape(name) + @":\s*#([0-9a-fA-F]{6});");
        m.Success.Should().BeTrue(name);
        int v = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0);
    }

    private static double Luminance((double R, double G, double B) c)
    {
        static double F(double x) => x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        return (0.2126 * F(c.R)) + (0.7152 * F(c.G)) + (0.0722 * F(c.B));
    }

    private static double Ratio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void TouchAndHaptics_DefaultOn_AndRoundTripWhenTurnedOff()
    {
        ClientSettings.Default.TouchControls.Should().BeTrue();
        ClientSettings.Default.Haptics.Should().BeTrue();

        ClientSettings off = ClientSettings.Default with { TouchControls = false, Haptics = false };
        ClientSettings back = ClientSettings.Parse(off.Serialize());

        back.TouchControls.Should().BeFalse();
        back.Haptics.Should().BeFalse();
    }
}
