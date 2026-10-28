// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Contracts.Validation;

namespace Nosum.Handlers.Security;

/// <summary>
/// The shared input validator (docs/adr/ADR-032 D3). A packet declares the bounds of its client-supplied members with
/// <see cref="WireMaxLengthAttribute"/>, <see cref="WireRangeAttribute"/> and <see cref="WireDefinedAttribute"/>; the first time a packet type
/// is checked the declarations are compiled into one delegate, so checking a packet reads its members and allocates nothing.
/// </summary>
public static class PacketBounds
{
    private static readonly ConcurrentDictionary<Type, Func<object, bool>?> s_checks = new();

    /// <summary>Checks a packet against the bounds its type declares.</summary>
    /// <param name="packet">The packet as received.</param>
    /// <returns><see langword="true"/> when every declared bound holds (also for a packet that declares none).</returns>
    public static bool IsWithinBounds(object packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Func<object, bool>? check = CheckFor(packet.GetType());
        return check is null || check(packet);
    }

    /// <summary>
    /// Lists the wire members of a packet type that need a bound but declare none: strings and collections need <see cref="WireMaxLengthAttribute"/>,
    /// signed and floating point numbers need <see cref="WireRangeAttribute"/>. Enums may add <see cref="WireDefinedAttribute"/> but need not: a service that answers an undefined value with a documented status (language, chat channel) keeps doing so. Members of nested row types
    /// are checked too. Booleans, bytes, GUIDs and unsigned numbers cannot be out of bounds and are exempt.
    /// </summary>
    /// <param name="packetType">A packet or row type.</param>
    /// <returns>One <c>Type.Member</c> entry per missing declaration; empty when the type is fully bounded.</returns>
    public static List<string> MissingDeclarations(Type packetType)
    {
        ArgumentNullException.ThrowIfNull(packetType);
        List<string> missing = [];
        Collect(packetType, missing, []);
        return missing;
    }

    private static void Collect(Type type, List<string> missing, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (PropertyInfo property in WireMembers(type))
        {
            Type t = property.PropertyType;
            if (t == typeof(string) || IsList(t, out _))
            {
                if (property.GetCustomAttribute<WireMaxLengthAttribute>() is null)
                {
                    missing.Add(type.Name + "." + property.Name);
                }

                if (IsList(t, out Type? element) && element is { IsClass: true } && element != typeof(string))
                {
                    Collect(element, missing, seen);
                }
            }
            else if (IsRangeType(t) && property.GetCustomAttribute<WireRangeAttribute>() is null)
            {
                missing.Add(type.Name + "." + property.Name);
            }
        }
    }

    private static Func<object, bool>? CheckFor(Type type) => s_checks.GetOrAdd(type, static t => Compile(t));

    private static Func<object, bool>? Compile(Type type)
    {
        ParameterExpression input = Expression.Parameter(typeof(object), "packet");
        ParameterExpression typed = Expression.Variable(type, "typed");
        List<Expression> tests = [];
        foreach (PropertyInfo property in WireMembers(type))
        {
            MemberExpression member = Expression.Property(typed, property);
            Type t = property.PropertyType;
            if (property.GetCustomAttribute<WireMaxLengthAttribute>() is { } length && t == typeof(string))
            {
                tests.Add(Expression.AndAlso(
                    Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                    Expression.LessThanOrEqual(Expression.Property(member, nameof(string.Length)), Expression.Constant(length.MaxLength))));
            }
            else if (property.GetCustomAttribute<WireMaxLengthAttribute>() is { } count && IsList(t, out Type? element))
            {
                Expression notNull = Expression.NotEqual(member, Expression.Constant(null, t));
                Expression within = Expression.LessThanOrEqual(Expression.Property(member, "Count"), Expression.Constant(count.MaxLength));
                Func<object, bool>? elementCheck = element is { IsClass: true } && element != typeof(string) ? CheckFor(element) : null;
                tests.Add(elementCheck is null
                    ? Expression.AndAlso(notNull, within)
                    : Expression.AndAlso(notNull, Expression.AndAlso(within, Expression.Call(typeof(PacketBounds), nameof(ElementsOk), [element!], member))));
            }
            else if (property.GetCustomAttribute<WireRangeAttribute>() is { } range && IsRangeType(t))
            {
                Expression value = Expression.Convert(member, typeof(double));
                tests.Add(Expression.AndAlso(
                    Expression.GreaterThanOrEqual(value, Expression.Constant(range.Minimum)),
                    Expression.LessThanOrEqual(value, Expression.Constant(range.Maximum))));
            }
            else if (t.IsEnum && property.GetCustomAttribute<WireDefinedAttribute>() is not null)
            {
                Type underlying = Enum.GetUnderlyingType(t);
                Expression value = Expression.Convert(member, underlying);
                Expression? any = null;
                foreach (object defined in Enum.GetValuesAsUnderlyingType(t))
                {
                    Expression equal = Expression.Equal(value, Expression.Constant(defined, underlying));
                    any = any is null ? equal : Expression.OrElse(any, equal);
                }

                tests.Add(any ?? Expression.Constant(false));
            }
        }

        if (tests.Count == 0)
        {
            return null;
        }

        Expression all = tests.Aggregate(Expression.AndAlso);
        BlockExpression body = Expression.Block([typed], Expression.Assign(typed, Expression.Convert(input, type)), all);
        return Expression.Lambda<Func<object, bool>>(body, input).Compile();
    }

    // Called from the compiled delegate for a collection of bounded rows.
    private static bool ElementsOk<TElement>(List<TElement> list)
        where TElement : class
    {
        Func<object, bool>? check = CheckFor(typeof(TElement));
        if (check is null)
        {
            return true;
        }

        for (int i = 0; i < list.Count; i++)
        {
            TElement row = list[i];
            if (row is null || !check(row))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<PropertyInfo> WireMembers(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.GetCustomAttributes().Any(static a => a.GetType().Name == "SerializeOrderAttribute"));

    private static bool IsList(Type type, out Type? element)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            element = type.GetGenericArguments()[0];
            return true;
        }

        element = null;
        return false;
    }

    private static bool IsRangeType(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte) || type == typeof(float) || type == typeof(double);
}
