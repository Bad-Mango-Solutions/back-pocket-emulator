// <copyright file="GuestOsSessionTrapHandler.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Cpu;

/// <summary>
/// Observes one guest-OS session trap while the instruction is still the entry opcode.
/// </summary>
/// <param name="address">The trap address, such as the DOS 3.3 file manager or RWTS.</param>
/// <param name="cpu">The CPU at entry. Registers and memory are still the caller's.</param>
/// <param name="at">The machine cycle.</param>
public delegate void GuestOsSessionTrapHandler(ushort address, ICpu cpu, Cycle at);