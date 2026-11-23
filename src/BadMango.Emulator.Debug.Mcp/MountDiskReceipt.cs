// <copyright file="MountDiskReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// The payload for <c>mount_disk</c>.
/// </summary>
/// <param name="Slot">The peripheral slot, 1 through 7.</param>
/// <param name="Drive">The 1-based drive number.</param>
/// <param name="Path">The sandbox path that was mounted, with symbolic links resolved.</param>
/// <param name="WriteProtect">Whether the image was opened read-only.</param>
public sealed record MountDiskReceipt(int Slot, int Drive, string Path, bool WriteProtect);