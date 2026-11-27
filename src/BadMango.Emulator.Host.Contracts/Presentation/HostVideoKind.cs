// <copyright file="HostVideoKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Which video output a machine publishes.
/// </summary>
/// <remarks>
/// <para>
/// Composite outputs emit a raw signal (dots and line flags). The client may decode it,
/// which is how two windows on one machine can show Green and Amber at the same time.
/// The host may also publish a default decoded BGRA frame. RGB outputs emit finished BGRA
/// and the display choice is locked. The streamable wire shape is issue #738.
/// </para>
/// <para>
/// The logical composite layout, once #410 lands, is 192 lines by 560 dots plus 192 line-flag
/// bytes (<c>Composite560Signal</c>). This contract does not require those dimensions. The
/// loopback accepts the sample the publisher supplies.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HostVideoKind>))]
public enum HostVideoKind
{
    /// <summary>
    /// Raw composite signal, optionally with a host-decoded BGRA frame.
    /// </summary>
    Composite,

    /// <summary>
    /// Finished BGRA. The monitor does not decode.
    /// </summary>
    Rgb,
}