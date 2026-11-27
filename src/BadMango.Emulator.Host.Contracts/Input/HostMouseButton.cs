// <copyright file="HostMouseButton.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// A mouse button.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HostMouseButton>))]
public enum HostMouseButton
{
    /// <summary>
    /// The left button.
    /// </summary>
    Left,

    /// <summary>
    /// The middle button.
    /// </summary>
    Middle,

    /// <summary>
    /// The right button.
    /// </summary>
    Right,
}