// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts;

/// <summary>
/// Wire protocol identity shared by the server and every client.
/// </summary>
public static class NosumProtocol
{
    /// <summary>
    /// Wire protocol version. Bump only on a breaking wire change (a removed or reordered
    /// packet member, a reassigned opcode); server and clients then ship together.
    /// </summary>
    public const ushort Version = 2;
}
