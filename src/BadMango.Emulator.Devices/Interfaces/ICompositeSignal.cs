// <copyright file="ICompositeSignal.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// Composite video signal: packed dots plus one set of line flags per line.
/// </summary>
/// <remarks>
/// <para>
/// The v1 composite layout is 192 lines of 560 dots. Each line is 9 64-bit words
/// (576 bits). Dot <c>x</c> is bit <c>x &amp; 63</c> of word <c>x &gt;&gt; 6</c>, LSB-first.
/// Bits 560–575 are zero. Sizes stay <see cref="int"/>, as specified.
/// </para>
/// <para>
/// Memory taken from <see cref="Dots"/> or <see cref="LineFlags"/> is valid only until the
/// lease that produced the signal is disposed.
/// </para>
/// </remarks>
public interface ICompositeSignal : IVideoSignal
{
    /// <summary>
    /// Gets the number of dots in one active line.
    /// </summary>
    /// <value>560 for the v1 composite output.</value>
    int DotsPerLine { get; }

    /// <summary>
    /// Gets the number of visible lines.
    /// </summary>
    /// <value>192 for the v1 composite output.</value>
    int Lines { get; }

    /// <summary>
    /// Gets the number of 64-bit words stored for each line.
    /// </summary>
    /// <value>9 for the v1 composite output.</value>
    int StrideWords { get; }

    /// <summary>
    /// Gets the packed dots, LSB-first within each word.
    /// </summary>
    /// <value>The dot buffer. Valid only while the producing lease is held.</value>
    ReadOnlyMemory<ulong> Dots { get; }

    /// <summary>
    /// Gets one <see cref="ScanlineFlags"/> value per line.
    /// </summary>
    /// <value>The per-line flags. Valid only while the producing lease is held.</value>
    ReadOnlyMemory<ScanlineFlags> LineFlags { get; }
}