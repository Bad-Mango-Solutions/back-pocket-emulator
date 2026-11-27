// <copyright file="HostStorageCategory.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Which kind of storage a drive is. The two kinds do not share a lifecycle.
/// </summary>
public enum HostStorageCategory
{
    /// <summary>
    /// Media the operator swaps while the machine is in use. Floppies today; CD-ROM, USB, and flash later.
    /// Library images use <c>library://disks/...</c>.
    /// </summary>
    RemovableMedia = 0,

    /// <summary>
    /// Storage that is part of the machine, the way a card is part of a slot. Hard disks and the volumes inside them.
    /// Attach and detach are legal only while the machine is off, and they take effect on the next power-on.
    /// Library images use <c>library://drives/...</c>.
    /// </summary>
    FixedStorage = 1,
}