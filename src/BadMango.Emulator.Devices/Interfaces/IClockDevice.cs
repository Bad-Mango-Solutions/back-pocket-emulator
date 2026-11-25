// <copyright file="IClockDevice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Clock device interface for host time access.
/// </summary>
/// <remarks>
/// <para>
/// This interface is implemented by slot cards like the Thunderclock Plus that
/// surface the host's date/time to the emulated system.
/// </para>
/// <para>
/// Clock cards allow emulated software (especially ProDOS) to maintain accurate
/// timestamps for file operations. Time comes from <see cref="IClockSource"/>, which the
/// slotless ProDOS clock implements directly. A clock card and the slotless clock therefore
/// share one time-source contract.
/// </para>
/// </remarks>
public interface IClockDevice : IClockSource, ISlotCard
{
}