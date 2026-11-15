// <copyright file="ScanFrame.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Placeholder for the per-hub scan buffer.
/// </summary>
/// <remarks>
/// A sealed class so the scanner contract can name it. The fetch records, line flags,
/// and line accessors are filled in when the scanner arrives. It is not handed to consumers.
/// </remarks>
public sealed class ScanFrame
{
}