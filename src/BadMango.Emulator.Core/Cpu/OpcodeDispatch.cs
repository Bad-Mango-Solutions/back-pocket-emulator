// <copyright file="OpcodeDispatch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

using Interfaces.Cpu;

/// <summary>
/// Dispatch target for one opcode. The call site passes the entry being executed.
/// </summary>
/// <param name="cpu">The CPU instance providing state and memory access.</param>
/// <param name="entry">The opcode entry for this call. Converted handlers may ignore it.</param>
/// <remarks>
/// Each concrete target is cached once as a <c>static readonly</c> instance.
/// The legacy path shares one trampoline instance. A converted opcode is another
/// static method with this signature, stored in its own static readonly field.
/// </remarks>
public delegate void OpcodeDispatch(ICpu cpu, in OpcodeEntry entry);