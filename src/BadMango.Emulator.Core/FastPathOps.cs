// <copyright file="FastPathOps.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Operations that may index a page's fast-path backing array.
/// </summary>
/// <remarks>
/// <para>
/// A bit is set only when that operation can be a direct array index. Missing bits stay on
/// the checked bus path. <see cref="Fetch"/> is for a native CPU instruction fetch;
/// the 65C02 treats fetch as a read and does not consult this flag.
/// </para>
/// </remarks>
[Flags]
public enum FastPathOps : byte
{
    /// <summary>
    /// No operation may index the backing array.
    /// </summary>
    None = 0,

    /// <summary>
    /// A data read may index the backing array.
    /// </summary>
    Read = 1 << 0,

    /// <summary>
    /// A data write may index the backing array. Set only for a <see cref="RamTarget"/>.
    /// </summary>
    Write = 1 << 1,

    /// <summary>
    /// A native instruction fetch may index the backing array.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="Read"/> and <see cref="PagePerms.Execute"/>. The 65C02 path does not
    /// consult this flag.
    /// </remarks>
    Fetch = 1 << 2,
}