// <copyright file="IProfileConfiguredDevice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

using System.Text.Json;

/// <summary>
/// A motherboard device that reads its profile <c>config</c> object.
/// </summary>
public interface IProfileConfiguredDevice
{
    /// <summary>
    /// Applies the profile entry's <c>config</c> object.
    /// </summary>
    /// <param name="config">
    /// The <c>devices.motherboard[].config</c> object, or <see langword="null"/> when the profile omits it.
    /// </param>
    void ApplyProfileConfig(JsonElement? config);
}