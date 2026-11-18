// <copyright file="PrivilegeLevel.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core;

/// <summary>
/// Defines the canonical privilege levels for the 65832 processor and bus checks.
/// </summary>
/// <remarks>
/// <para>
/// Privilege levels are encoded in bits 0-1 of CR0 (Control Register 0) with the
/// architectural ordering <c>User (0) &lt; Kernel (1) &lt; Hypervisor (2)</c>.
/// </para>
/// <para>
/// Permission checks compare the running level against a required minimum with
/// <c>current &gt;= required</c>, so a <see cref="User"/> minimum allows every level.
/// </para>
/// </remarks>
public enum PrivilegeLevel : byte
{
    /// <summary>User mode - lowest privilege, memory protection enforced.</summary>
    User = 0,

    /// <summary>Kernel mode - full access to system resources and privileged instructions.</summary>
    Kernel = 1,

    /// <summary>Hypervisor mode - manages virtual machines and guest kernels (reserved).</summary>
    /// <remarks>
    /// Reserved for future virtualization support. Systems without H mode
    /// treat this as equivalent to Kernel mode.
    /// </remarks>
    Hypervisor = 2,

    /// <summary>Reserved for future use.</summary>
    Reserved = 3,
}