// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Data;

/// <summary>Creates units of work. Registered once; services call it per operation.</summary>
public interface IUnitOfWorkFactory
{
    /// <summary>Creates a new unit of work. The caller disposes it.</summary>
    /// <returns>A fresh unit of work.</returns>
    IUnitOfWork Create();
}
