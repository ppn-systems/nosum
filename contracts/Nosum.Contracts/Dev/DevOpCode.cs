// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Dev;

/// <summary>
/// Opcodes of the Dev domain: tester tools that exist only when the host runs in the Development environment.
/// <para>Band <c>0xF000-0xF0FF</c>. The server registers the handler only when <c>DOTNET_ENVIRONMENT</c> is <c>Development</c>; in Production the opcode is unknown and the frame is dropped. The client sends it only from a build made with <c>-p:NosumDevTools=true</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class DevOpCode
{
    /// <summary>Gives the sender items and/or coins out of thin air (development only). Reply: <see cref="S2C.DevGrantResult"/>.</summary>
    public const ushort DevGrant = 0xF000;

    /// <summary>Reply to <see cref="DevGrant"/>.</summary>
    public const ushort DevGrantResult = 0xF040;
}
