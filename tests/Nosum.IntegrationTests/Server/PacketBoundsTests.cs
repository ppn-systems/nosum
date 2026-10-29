// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Common;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Contracts.Validation;
using Nosum.Handlers.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The shared input validator (docs/adr/ADR-032 D3): declared bounds hold for good values and reject oversized and malformed ones.</summary>
public sealed class PacketBoundsTests
{
    [Fact]
    public void IsWithinBounds_OrdinaryPackets_Pass()
    {
        Within(() => new ExternalSignIn { Provider = ExternalProvider.Google, IdToken = "fake:Google:player123" }).Should().BeTrue();
        Within(() => new SendChat { Channel = ChatChannel.Local, Text = "hello", Target = string.Empty }).Should().BeTrue();
        Within(() => new MoveIntent { X = 120.5f, Y = -3f }).Should().BeTrue();
        Within(() => new CreateListing { Slot = 3, ItemId = 4, Quantity = 20, Price = 5_000 }).Should().BeTrue();
    }

    [Fact]
    public void IsWithinBounds_OversizedString_Fails()
    {
        Within(() => new ExternalSignIn { Provider = ExternalProvider.Google, IdToken = new string('a', WireLimits.IdToken + 1) }).Should().BeFalse();
        Within(() => new LinkExternalIdentity { Provider = ExternalProvider.Google, IdToken = "x", Nickname = new string('n', WireLimits.ShortText + 1) }).Should().BeFalse();
        Within(() => new SendChat { Text = new string('t', WireLimits.ChatText + 1) }).Should().BeFalse();
    }

    [Fact]
    public void IsWithinBounds_NumbersOutsideTheirRange_Fail()
    {
        Within(() => new MoveIntent { X = float.NaN }).Should().BeFalse();
        Within(() => new MoveIntent { Y = float.PositiveInfinity }).Should().BeFalse();
        Within(() => new CreateListing { Quantity = -1 }).Should().BeFalse();
        Within(() => new CreateListing { Price = long.MaxValue }).Should().BeFalse();
    }

    [Fact]
    public void IsWithinBounds_UndefinedEnumWhereDeclared_Fails()
    {
        Within(() => new ChestMove { FromSide = (ChestSide)200 }).Should().BeFalse();
        Within(() => new ChestMove { FromSide = ChestSide.Chest, ToSide = ChestSide.Player }).Should().BeTrue();
    }

    [Fact]
    public void IsWithinBounds_TooLongOrBadCollection_Fails()
    {
        Within(() => new BrowseListings { ItemIds = Enumerable.Range(0, WireLimits.MaxFilterEntries + 1).ToList() }).Should().BeFalse();
        Within(() => new TradeSetOffer { Items = Enumerable.Range(0, WireLimits.MaxOfferLines + 1).Select(i => new TradeItemRow { ItemId = i, Quantity = 1 }).ToList() }).Should().BeFalse();
        Within(() => new TradeSetOffer { Items = [new TradeItemRow { ItemId = 1, Quantity = int.MaxValue }] }).Should().BeFalse();
        Within(() => new TradeSetOffer { Items = [new TradeItemRow { ItemId = 1, Quantity = 3 }], Coins = 10 }).Should().BeTrue();
    }

    [Fact]
    public void EveryClientPacket_Fuzzed_RejectsEachViolationAndAcceptsTheDefault()
    {
        Type[] packets = ClientPackets();
        packets.Should().HaveCountGreaterThan(50);
        int violations = 0;
        foreach (Type type in packets)
        {
            PacketBounds.MissingDeclarations(type).Should().BeEmpty("{0} must bound every client-supplied member", type.Name);

            using (IDisposable baseline = (IDisposable)Activator.CreateInstance(type)!)
            {
                PacketBounds.IsWithinBounds(baseline).Should().BeTrue(type.Name + " with default values");
            }

            foreach (PropertyInfo property in type.GetProperties().Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name == "SerializeOrderAttribute")))
            {
                foreach (object hostile in Hostile(property))
                {
                    using IDisposable packet = (IDisposable)Activator.CreateInstance(type)!;
                    property.SetValue(packet, hostile);
                    PacketBounds.IsWithinBounds(packet).Should().BeFalse("{0}.{1} = {2}", type.Name, property.Name, Describe(hostile));
                    violations++;
                }
            }
        }

        violations.Should().BeGreaterThan(80);
    }

    private static bool Within<T>(Func<T> make)
        where T : IDisposable
    {
        using T packet = make();
        return PacketBounds.IsWithinBounds(packet);
    }

    private static Type[] ClientPackets()
        => typeof(AccountOpCode).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.EndsWith(".C2S", StringComparison.Ordinal) && t is { IsClass: true, IsAbstract: false })
            .Where(t => !RetiredPackets.Types.Contains(t))
            .ToArray();

    // Values that break the declared bound of a member; empty for members that declare none.
    private static IEnumerable<object> Hostile(PropertyInfo property)
    {
        if (property.GetCustomAttribute<WireMaxLengthAttribute>() is { } length)
        {
            if (property.PropertyType == typeof(string))
            {
                yield return new string('x', length.MaxLength + 1);
            }
            else
            {
                Type element = property.PropertyType.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(property.PropertyType)!;
                for (int i = 0; i <= length.MaxLength; i++)
                {
                    _ = list.Add(element == typeof(int) ? i : Activator.CreateInstance(element));
                }

                yield return list;
            }
        }

        if (property.GetCustomAttribute<WireRangeAttribute>() is { } range)
        {
            Type t = property.PropertyType;
            if (t == typeof(float))
            {
                yield return float.NaN;
                yield return float.PositiveInfinity;
                yield return (float)(range.Maximum * 2);
            }
            else if (t == typeof(long))
            {
                yield return range.Maximum >= long.MaxValue / 2 ? long.MaxValue : (long)range.Maximum + 1;
                yield return range.Minimum <= long.MinValue / 2 ? long.MinValue : (long)range.Minimum - 1;
            }
            else
            {
                if (range.Maximum < int.MaxValue)
                {
                    yield return (int)range.Maximum + 1;
                }

                if (range.Minimum > int.MinValue)
                {
                    yield return (int)range.Minimum - 1;
                }
            }
        }

        if (property.GetCustomAttribute<WireDefinedAttribute>() is not null)
        {
            Type underlying = Enum.GetUnderlyingType(property.PropertyType);
            object undefined = Enum.ToObject(property.PropertyType, Convert.ChangeType(200, underlying, System.Globalization.CultureInfo.InvariantCulture));
            if (!Enum.IsDefined(property.PropertyType, undefined))
            {
                yield return undefined;
            }
        }
    }

    private static string Describe(object value)
        => value is string s ? "string of " + s.Length : value is ICollection c ? "collection of " + c.Count : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?";
}
