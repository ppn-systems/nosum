// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Fishing.C2S;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Fishing;
using Nosum.Wasm.ViewModels.Fishing;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The fishing banner's state machine (docs/adr/ADR-071 D4, D9): the bite comes after the server's delay, the press reels in during the window, the
/// replies end or keep the cast, and the banner's timers run on an injected clock.
/// </summary>
[Collection(SharedUiCulture.Name)]
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class FishingViewModelTests
{
    private const int BiteMs = 2000;
    private const int WindowMs = 800;

    [Fact]
    public void Cast_OkReply_BitesAfterTheBiteDelay()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7));

        rig.Vm.Cast(3, 4);
        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs - 1));
        rig.Vm.Phase.Should().Be(FishingPhase.Waiting, "the fish has not bitten before its delay");

        rig.Time.Advance(TimeSpan.FromMilliseconds(1));

        rig.Vm.Phase.Should().Be(FishingPhase.Biting);
        rig.Vm.BarMs.Should().Be(WindowMs, "the bar runs for the whole window");
        rig.Vm.Message.Should().Be("Something is biting!");
    }

    [Fact]
    public void Press_DuringTheBite_SendsReelInForTheCast()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7), new ReelResult { Status = FishingStatus.Nothing });
        rig.Vm.Cast(3, 4);
        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs));

        rig.Vm.Press();

        rig.Reels.Should().Equal(7u);
        rig.Vm.Phase.Should().Be(FishingPhase.Result, "the nothing reply ends the cast");
    }

    [Fact]
    public void Press_DuringTheWait_SendsNothingAndSaysNotYet()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7));
        rig.Vm.Cast(3, 4);

        rig.Vm.Press();

        rig.Reels.Should().BeEmpty("the fish has not bitten");
        rig.Vm.Phase.Should().Be(FishingPhase.Waiting);
        rig.Vm.Note.Should().Be("Not yet. Wait for the bite.");
    }

    [Fact]
    public void Press_WhenNoLineIsOut_DoesNothing()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7));

        rig.Vm.Press();

        rig.Reels.Should().BeEmpty();
        rig.Vm.Phase.Should().Be(FishingPhase.Idle);
    }

    [Fact]
    public void Cast_WhileALineIsOut_IsIgnored()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7));
        rig.Vm.Cast(3, 4);

        rig.Vm.Cast(5, 6);

        rig.Casts.Should().Equal((3, 4));
    }

    [Fact]
    public void Reel_TooEarly_KeepsTheCastOpenAndOpensTheWindowAgain()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7), new ReelResult { Status = FishingStatus.TooEarly });
        rig.Vm.Cast(3, 4);
        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs));
        rig.Vm.Press();

        rig.Vm.Phase.Should().Be(FishingPhase.Waiting, "the cast stays open after a press that came too early");
        rig.Vm.LineOut.Should().BeTrue();
        rig.Vm.Note.Should().Be("The fish has not bitten yet. Wait a moment.");

        rig.Time.Advance(FishingViewModel.TooEarlyRetry);

        rig.Vm.Phase.Should().Be(FishingPhase.Biting, "the window is still open and the banner shows it again");
        rig.Reels.Should().Equal(7u);
    }

    [Fact]
    public void Reel_Escaped_EndsTheCast()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7), new ReelResult { Status = FishingStatus.Escaped });
        rig.Vm.Cast(3, 4);
        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs));

        rig.Vm.Press();

        rig.Vm.Phase.Should().Be(FishingPhase.Result);
        rig.Vm.Message.Should().Be("The fish got away!");
        rig.Vm.LineOut.Should().BeFalse();
    }

    [Fact]
    public void Window_PassesWithoutAPress_EndsAsEscaped()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7));
        rig.Vm.Cast(3, 4);

        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs + WindowMs));

        rig.Vm.Phase.Should().Be(FishingPhase.Result);
        rig.Vm.Message.Should().Be("The fish got away!");
        rig.Reels.Should().BeEmpty("nothing was pressed");
    }

    [Fact]
    public void Reel_Caught_NamesTheFish()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(CastOk(castId: 7), new ReelResult { Status = FishingStatus.Ok, ItemId = ItemCatalog.Carp, Quantity = 1, XpGained = 10 });
        rig.Vm.Cast(3, 4);
        rig.Time.Advance(TimeSpan.FromMilliseconds(BiteMs));

        rig.Vm.Press();

        rig.Vm.Phase.Should().Be(FishingPhase.Result);
        rig.Vm.Message.Should().Be("You caught Carp!");
    }

    [Fact]
    public void Cast_RefusedWithNoRod_EndsWithItsText()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(new CastResult { Status = FishingStatus.NoRod });

        rig.Vm.Cast(3, 4);

        rig.Vm.Phase.Should().Be(FishingPhase.Result);
        rig.Vm.Message.Should().Be("You are not holding a fishing rod.");
        rig.Vm.LineOut.Should().BeFalse();
    }

    [Fact]
    public void Cast_ConnectionDropped_EndsAsAConflict()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(cast: null);

        rig.Vm.Cast(3, 4);

        rig.Vm.Phase.Should().Be(FishingPhase.Result, "a cast with no reply did not go through");
        rig.Vm.Message.Should().Be("Try again.");
    }

    [Fact]
    public void Result_ClearsAfterItsLinger()
    {
        using CultureScope scope = new("en");
        Rig rig = Create(new CastResult { Status = FishingStatus.NotWater });
        rig.Vm.Cast(3, 4);

        rig.Time.Advance(FishingViewModel.ResultLinger - TimeSpan.FromMilliseconds(1));
        rig.Vm.Phase.Should().Be(FishingPhase.Result);

        rig.Time.Advance(TimeSpan.FromMilliseconds(1));

        rig.Vm.Phase.Should().Be(FishingPhase.Idle);
    }

    [Fact]
    public void Language_FishingStatusesAndBannerText_ExistInBothLanguages()
    {
        string[] needed =
        [
            .. Enum.GetValues<FishingStatus>().Where(s => s != FishingStatus.Ok).Select(s => "Fishing_Status_" + s),
            "Fishing_Title",
            "Fishing_Casting",
            "Fishing_Waiting",
            "Fishing_Bite",
            "Fishing_Press",
            "Fishing_Reeling",
            "Fishing_NotYet",
            "Fishing_Caught",
            "Skill_Fishing",
        ];

        Keys("Language.resx").Should().Contain(needed);
        Keys("Language.en.resx").Should().Contain(needed);
    }

    private static Rig Create(CastResult? cast, ReelResult? reel = null)
    {
        FakeGameSocket socket = new();
        Rig rig = new();
        if (cast is not null)
        {
            socket.ReplyWith<CastResult>(request =>
            {
                CastRod rod = (CastRod)request;
                rig.Casts.Add((rod.TileX, rod.TileY));
                return cast;
            });
        }

        socket.ReplyWith<ReelResult>(request =>
        {
            ReelIn press = (ReelIn)request;
            rig.Reels.Add(press.CastId);
            return reel ?? new ReelResult { Status = FishingStatus.Nothing };
        });
        rig.Time = new ManualTime();
        rig.Vm = new FishingViewModel(new FishingChannel(socket), Describer(), Text(), rig.Time);
        return rig;
    }

    private static CastResult CastOk(uint castId) => new()
    {
        Status = FishingStatus.Ok,
        CastId = castId,
        BiteDelayMs = BiteMs,
        WindowMs = WindowMs,
    };

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    // What one test's socket recorded and the view model it drives.
    private sealed class Rig
    {
        public FishingViewModel Vm { get; set; } = null!;

        public ManualTime Time { get; set; } = null!;

        public List<(int X, int Y)> Casts { get; } = [];

        public List<uint> Reels { get; } = [];
    }

    // A clock the tests move by hand: timers fire, in order, when the clock is advanced past their due time.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public DateTimeOffset Now { get; private set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(this, callback, state, this.Now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target = this.Now + by;
            while (true)
            {
                ManualTimer? next = _timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                _timers.Remove(next);
                this.Now = next.Due;
                next.Callback(next.State);
            }

            this.Now = target;
        }

        internal void Remove(ManualTimer timer) => _timers.Remove(timer);

        internal sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            public TimerCallback Callback { get; } = callback;

            public object? State { get; } = state;

            public DateTimeOffset Due { get; } = due;

            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("The fishing banner never changes a timer in place.");

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                this.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
