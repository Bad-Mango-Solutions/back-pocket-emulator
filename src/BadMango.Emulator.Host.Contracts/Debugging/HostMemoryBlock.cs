// <copyright file="HostMemoryBlock.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Bytes read through the debugger extension. The loopback copies them out of its stub memory.
/// </summary>
/// <param name="Address">The first address.</param>
/// <param name="Data">The bytes. The caller may mutate this copy.</param>
public sealed record HostMemoryBlock(uint Address, ReadOnlyMemory<byte> Data);