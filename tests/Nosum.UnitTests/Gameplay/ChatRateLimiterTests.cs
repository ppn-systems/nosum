// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Chat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class ChatRateLimiterTests
{
    [Fact]
    public void TryTake_FirstThreeMessages_PassBackToBack()
    {
        ChatRateLimiter limiter = new();

        limiter.TryTake(0).Should().BeTrue();
        limiter.TryTake(0).Should().BeTrue();
        limiter.TryTake(0).Should().BeTrue();
    }

    [Fact]
    public void TryTake_FourthMessageInTheSameMoment_IsRefused()
    {
        ChatRateLimiter limiter = new();
        for (int i = 0; i < ChatRateLimiter.Burst; i++)
        {
            _ = limiter.TryTake(1_000);
        }

        limiter.TryTake(1_000).Should().BeFalse("the burst is three");
        limiter.TryTake(1_100).Should().BeFalse("a tenth of a second refills a twentieth of a message");
    }

    [Fact]
    public void TryTake_AfterTheBurst_OneMessageEveryTwoSecondsIsAllowed()
    {
        ChatRateLimiter limiter = new();
        for (int i = 0; i < ChatRateLimiter.Burst; i++)
        {
            _ = limiter.TryTake(0);
        }

        // 5 messages per 10 s is one token per 2 s.
        limiter.TryTake(1_999).Should().BeFalse();
        limiter.TryTake(2_000).Should().BeTrue();
        limiter.TryTake(2_000).Should().BeFalse();
        limiter.TryTake(4_000).Should().BeTrue();
    }

    [Fact]
    public void TryTake_SteadyStream_NeverExceedsFiveMessagesInAnyTenSeconds()
    {
        ChatRateLimiter limiter = new();
        int allowed = 0;

        // A flood: a message every 100 ms for a minute.
        for (long t = 0; t < 60_000; t += 100)
        {
            if (limiter.TryTake(t))
            {
                allowed++;
            }
        }

        // Burst of 3 plus one per two seconds: 3 + 29 over 60 s.
        allowed.Should().BeInRange(30, 33);
        allowed.Should().BeLessThanOrEqualTo(3 + (60 / 2));
    }

    [Fact]
    public void TryTake_AfterALongPause_TheBucketIsFullButNeverLargerThanTheBurst()
    {
        ChatRateLimiter limiter = new();
        for (int i = 0; i < ChatRateLimiter.Burst; i++)
        {
            _ = limiter.TryTake(0);
        }

        long later = 10 * ChatRateLimiter.WindowMs;
        limiter.TryTake(later).Should().BeTrue();
        limiter.TryTake(later).Should().BeTrue();
        limiter.TryTake(later).Should().BeTrue();
        limiter.TryTake(later).Should().BeFalse("an hour of silence still buys only the burst");
    }

    [Fact]
    public void TryTake_ClockGoingBackwards_DoesNotRefill()
    {
        ChatRateLimiter limiter = new();
        for (int i = 0; i < ChatRateLimiter.Burst; i++)
        {
            _ = limiter.TryTake(10_000);
        }

        limiter.TryTake(5_000).Should().BeFalse();
    }
}
