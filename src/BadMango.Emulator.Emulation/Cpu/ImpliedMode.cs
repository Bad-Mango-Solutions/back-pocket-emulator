// <copyright file="ImpliedMode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>Generic implied addressing mode resolver.</summary>
public readonly struct ImpliedMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Implied(cpu);
}

/// <summary>Generic immediate addressing mode resolver.</summary>
public readonly struct ImmediateMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Immediate(cpu);
}

/// <summary>Generic zero-page addressing mode resolver.</summary>
public readonly struct ZeroPageMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPage(cpu);
}

/// <summary>Generic zero-page,X addressing mode resolver.</summary>
public readonly struct ZeroPageXMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPageX(cpu);
}

/// <summary>Generic zero-page,Y addressing mode resolver.</summary>
public readonly struct ZeroPageYMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPageY(cpu);
}

/// <summary>Generic absolute addressing mode resolver.</summary>
public readonly struct AbsoluteMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Absolute(cpu);
}

/// <summary>Generic absolute,X addressing mode resolver.</summary>
public readonly struct AbsoluteXMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.AbsoluteX(cpu);
}

/// <summary>Generic absolute,Y addressing mode resolver.</summary>
public readonly struct AbsoluteYMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.AbsoluteY(cpu);
}

/// <summary>Generic indirect,X addressing mode resolver.</summary>
public readonly struct IndirectXMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.IndirectX(cpu);
}

/// <summary>Generic indirect,Y addressing mode resolver.</summary>
public readonly struct IndirectYMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.IndirectY(cpu);
}

/// <summary>Generic absolute,X write-cycle addressing mode resolver.</summary>
public readonly struct AbsoluteXWriteMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.AbsoluteXWrite(cpu);
}

/// <summary>Generic absolute,Y write-cycle addressing mode resolver.</summary>
public readonly struct AbsoluteYWriteMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.AbsoluteYWrite(cpu);
}

/// <summary>Generic indirect,Y write-cycle addressing mode resolver.</summary>
public readonly struct IndirectYWriteMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.IndirectYWrite(cpu);
}

/// <summary>Generic accumulator addressing mode resolver.</summary>
public readonly struct AccumulatorMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Accumulator(cpu);
}

/// <summary>Generic relative addressing mode resolver.</summary>
public readonly struct RelativeMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Relative(cpu);
}

/// <summary>Generic indirect addressing mode resolver.</summary>
public readonly struct IndirectMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Indirect(cpu);
}

/// <summary>Generic zero-page indirect addressing mode resolver.</summary>
public readonly struct ZeroPageIndirectMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPageIndirect(cpu);
}

/// <summary>Generic absolute indirect,X addressing mode resolver.</summary>
public readonly struct AbsoluteIndirectXMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.AbsoluteIndirectX(cpu);
}

/// <summary>Generic zero-page relative addressing mode resolver.</summary>
public readonly struct ZeroPageRelativeMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPageRelative(cpu);
}