// <copyright file="AbortReaction.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

/// <summary>
/// How a 65C02 treats an asserted <c>SignalLine.Abort</c>.
/// </summary>
/// <remarks>
/// Neither value is a 6502 instruction. <see cref="NoOp"/> is the boot default so an
/// unmapped fetch still stops with <see cref="HaltState.Stp"/>. <see cref="HostBreak"/>
/// is a debugger setting. A 65816 does not use this property; it takes the bank-0
/// abort vectors instead.
/// </remarks>
public enum AbortReaction
{
    /// <summary>
    /// Ignore the line. Fault status and address stay latched. Execution continues,
    /// including <see cref="HaltState.Stp"/> on an unmapped fetch.
    /// </summary>
    NoOp = 0,

    /// <summary>
    /// At the next instruction boundary, request a host stop. Do not set
    /// <see cref="HaltState.Stp"/>, push a vector, or change registers.
    /// </summary>
    HostBreak = 1,
}