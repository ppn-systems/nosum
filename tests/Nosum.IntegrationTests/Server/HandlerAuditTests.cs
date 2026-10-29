// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Handlers.Account;
using Nosum.Handlers.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The handler audit of docs/adr/ADR-032 D2: every packet handler declares who may call it, how often, and the bounds of what it receives.
/// A new handler that forgets one of them fails the build here, before it can be reached from the network.
/// </summary>
public sealed class HandlerAuditTests
{
    // Handlers a caller may reach without being signed in. Adding to this list is a security review decision.
    private static readonly HashSet<ushort> s_publicOpcodes =
    [
        AccountOpCode.Register,
        AccountOpCode.Login,
        AccountOpCode.ResumeSession,
        AccountOpCode.StartGuest,
    ];

    private static readonly string[] s_secretMembers = ["Password", "CurrentPassword", "NewPassword", "SessionToken", "Code"];

    [Fact]
    public void EveryHandler_DeclaresPermissionRateLimitAndBoundedPacket()
    {
        List<string> problems = [];
        foreach (HandlerInfo handler in Handlers())
        {
            string name = handler.Class.Name + "." + handler.Method.Name;
            if (handler.Method.GetCustomAttribute<PacketPermissionAttribute>() is null)
            {
                problems.Add(name + ": no [PacketPermission]");
            }

            if (handler.Method.GetCustomAttribute<PacketRateLimitAttribute>() is null)
            {
                problems.Add(name + ": no [PacketRateLimit]");
            }

            foreach (string member in PacketBounds.MissingDeclarations(handler.Packet))
            {
                problems.Add(name + ": unbounded " + member);
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Handlers_ReachableWithoutSigningIn_AreExactlyTheReviewedOnes()
    {
        HashSet<ushort> open = Handlers()
            .Where(h => h.Method.GetCustomAttribute<PacketPermissionAttribute>()!.Level == PermissionLevel.NONE)
            .Select(h => h.Opcode)
            .ToHashSet();

        open.Should().BeEquivalentTo(s_publicOpcodes);
    }

    [Fact]
    public void Handlers_ReceivingASecret_RequireAnEncryptedFrame()
    {
        List<string> plain = [];
        foreach (HandlerInfo handler in Handlers())
        {
            bool carriesSecret = handler.Packet.GetProperties().Any(p => s_secretMembers.Contains(p.Name, StringComparer.Ordinal));
            if (carriesSecret && handler.Method.GetCustomAttribute<PacketEncryptionAttribute>() is not { IsEncrypted: true })
            {
                plain.Add(handler.Class.Name + "." + handler.Method.Name);
            }
        }

        plain.Should().BeEmpty();
    }

    [Fact]
    public void EveryClientPacket_HasOneHandler_AndEveryOpcodeIsUnique()
    {
        List<HandlerInfo> handlers = Handlers().ToList();
        handlers.Select(h => h.Opcode).Should().OnlyHaveUniqueItems();

        HashSet<Type> handled = handlers.Select(h => h.Packet).ToHashSet();
        Type[] clientPackets = typeof(AccountOpCode).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.EndsWith(".C2S", StringComparison.Ordinal) && t is { IsClass: true, IsAbstract: false })
            .Where(t => !RetiredPackets.Types.Contains(t))
            .ToArray();

        clientPackets.Should().NotBeEmpty();
        clientPackets.Where(t => !handled.Contains(t)).Select(t => t.Name).Should().BeEmpty();
    }

    [Fact]
    public void EveryHandlerClass_IsMappedByTheHost()
    {
        string program = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Nosum.Host", "Program.cs"));
        HashSet<string> mapped = program.Split("MapHandlers(typeof(", StringSplitOptions.None).Skip(1)
            .Select(s => s[..s.IndexOf(')', StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);

        Handlers().Select(h => h.Class.Name).Distinct().Where(n => !mapped.Contains(n)).Should().BeEmpty(
            "an unregistered handler never runs, silently");
    }

    private static IEnumerable<HandlerInfo> Handlers()
    {
        foreach (Type type in typeof(AccountHandlers).Assembly.GetTypes().Where(t => t.GetCustomAttribute<PacketHandlerAttribute>() is not null))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                PacketOpcodeAttribute? opcode = method.GetCustomAttribute<PacketOpcodeAttribute>();
                if (opcode is null)
                {
                    continue;
                }

                Type context = method.GetParameters()[0].ParameterType;
                yield return new HandlerInfo(type, method, opcode.OpCode, context.GetGenericArguments()[0]);
            }
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed record HandlerInfo(Type Class, MethodInfo Method, ushort Opcode, Type Packet);
}
