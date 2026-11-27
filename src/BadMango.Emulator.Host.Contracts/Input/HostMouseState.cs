// <copyright file="HostMouseState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The last mouse position, buttons, and which window holds capture.
/// </summary>
/// <param name="MotionKind">Whether <paramref name="X"/> and <paramref name="Y"/> are a delta or a position.</param>
/// <param name="X">The last X value.</param>
/// <param name="Y">The last Y value.</param>
/// <param name="Left">Whether the left button is down.</param>
/// <param name="Middle">Whether the middle button is down.</param>
/// <param name="Right">Whether the right button is down.</param>
/// <param name="CaptureWindowId">The window that holds capture, or <see langword="null"/> when the pointer is released.</param>
public sealed record HostMouseState(
    HostMouseMotionKind MotionKind,
    float X,
    float Y,
    bool Left,
    bool Middle,
    bool Right,
    string? CaptureWindowId);