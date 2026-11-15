// <copyright file="MonochromeOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Options for a monochrome monitor. v1 adds no members beyond <see cref="FrameProviderOptions"/>.
/// </summary>
public sealed record MonochromeOptions : FrameProviderOptions;