// <copyright file="FaultKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Defines the types of faults that can occur during bus operations.
/// </summary>
/// <remarks>
/// <para>
/// The fault model provides first-class return values from bus operations,
/// allowing the CPU to react correctly to access failures without relying
/// on exceptions. The bus never silently fixes faults and the CPU never
/// guesses why an access failed.
/// </para>
/// <para>
/// NX faults are enforced only on instruction fetch intent. Data reads
/// on NX pages are allowed unless other permissions forbid it.
/// </para>
/// </remarks>
[Flags]
public enum FaultKind : ushort
{
    /// <summary>
    /// No fault occurred; the operation completed successfully.
    /// </summary>
    None = 0x0000,

    /// <summary>
    /// The address has no page entry or maps to a memory hole.
    /// </summary>
    /// <remarks>
    /// Corresponds to a "translation fault" or "bus error" in the CPU's
    /// exception model.
    /// </remarks>
    Unmapped = 0x0001,

    /// <summary>
    /// The requested operation violates page permissions (read/write not allowed).
    /// </summary>
    /// <remarks>
    /// Corresponds to a "data access fault" in the CPU's exception model.
    /// </remarks>
    Permission = 0x0002,

    /// <summary>
    /// Execute permission denied on instruction fetch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fault triggers only when <see cref="AccessIntent.InstructionFetch"/>
    /// is attempted on a page without execute permission. Data reads on the
    /// same page are allowed.
    /// </para>
    /// <para>
    /// Corresponds to an "instruction abort" or "execute access fault" in the
    /// CPU's exception model.
    /// </para>
    /// <para>
    /// In Compat mode, NX enforcement is ignored per policy.
    /// </para>
    /// </remarks>
    Nx = 0x0004,

    /// <summary>
    /// The access violates alignment requirements for atomic wide operations.
    /// </summary>
    /// <remarks>
    /// Applies only when Native mode enforces alignment rules for atomic
    /// 16-bit or 32-bit operations.
    /// </remarks>
    Misaligned = 0x0008,

    /// <summary>
    /// A device signaled a fault during the access.
    /// </summary>
    /// <remarks>
    /// Used for "bus error"-style conditions where the device itself reports
    /// an error, such as accessing an unimplemented register or an illegal
    /// DMA operation.
    /// </remarks>
    DeviceFault = 0x0010,

    /// <summary>
    /// The bus fault was raised while attempting a write operation.
    /// </summary>
    Write = 0x0100,

    /// <summary>
    /// The bus fault was raised while attempting an instruction fetch.
    /// </summary>
    Fetch = 0x0200,

    /// <summary>
    /// The bus fault was raised when crossing a page bank boundary.
    /// </summary>
    BankCrossing = 0x0400,

    /// <summary>
    /// The bus fault was raised because the current privilege level was insufficient.
    /// </summary>
    Privilege = 0x0800,
}