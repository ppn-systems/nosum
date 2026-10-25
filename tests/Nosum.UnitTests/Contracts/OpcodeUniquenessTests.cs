// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>Opcodes are the wire's identity (docs/adr/ADR-003): two packets, or two opcode constants, sharing a value would be routed to the wrong handler, so the whole contracts assembly is checked at once.</summary>
public sealed class OpcodeUniquenessTests
{
    private static readonly Assembly s_contracts = typeof(FarmOpCode).Assembly;

    [Fact]
    public void PacketClasses_NeverShareAnOpcode()
    {
        List<(ushort OpCode, string Packet)> packets = [];
        foreach (Type type in s_contracts.GetTypes())
        {
            PropertyInfo? property = type.GetProperty("StaticOpCode", BindingFlags.Public | BindingFlags.Static);
            if (property?.PropertyType == typeof(ushort))
            {
                packets.Add(((ushort)property.GetValue(null)!, type.FullName!));
            }
        }

        packets.Should().NotBeEmpty();
        string[] clashes = [.. packets.GroupBy(p => p.OpCode).Where(g => g.Count() > 1).Select(g => $"0x{g.Key:X4}: {string.Join(", ", g.Select(p => p.Packet))}")];
        clashes.Should().BeEmpty("every packet class has its own opcode");
    }

    [Fact]
    public void OpcodeConstants_NeverShareAValue_AcrossTheOpcodeClasses()
    {
        List<(ushort OpCode, string Name)> constants = [];
        foreach (Type type in s_contracts.GetTypes().Where(t => t.IsAbstract && t.IsSealed && t.Name.EndsWith("OpCode", StringComparison.Ordinal)))
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(ushort)))
            {
                constants.Add(((ushort)field.GetRawConstantValue()!, $"{type.Name}.{field.Name}"));
            }
        }

        constants.Should().NotBeEmpty();
        string[] clashes = [.. constants.GroupBy(c => c.OpCode).Where(g => g.Count() > 1).Select(g => $"0x{g.Key:X4}: {string.Join(", ", g.Select(c => c.Name))}")];
        clashes.Should().BeEmpty("no value is used by two opcode constants");
    }
}
