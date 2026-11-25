// <copyright file="IMachineBuiltCallback.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Notified once the machine, its devices, and its component factories have been assembled.
/// </summary>
/// <remarks>
/// Component factories run after device initialization. A device that must see a component
/// created by a factory, such as the guest-OS detector, binds here.
/// </remarks>
public interface IMachineBuiltCallback
{
    /// <summary>
    /// Binds to components that exist only after <see cref="MachineBuilder"/> finishes.
    /// </summary>
    /// <param name="machine">The built machine.</param>
    void OnMachineBuilt(IMachine machine);
}