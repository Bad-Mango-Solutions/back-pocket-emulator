// <copyright file="HostMouseMotionKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Whether a mouse move is a delta or a position. The AppleMouse card is a later device; the input contract carries both forms now.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HostMouseMotionKind>))]
public enum HostMouseMotionKind
{
    /// <summary>
    /// <c>X</c> and <c>Y</c> are deltas from the previous position.
    /// </summary>
    Relative,

    /// <summary>
    /// <c>X</c> and <c>Y</c> are positions in the range 0 to 1.
    /// </summary>
    Absolute,
}