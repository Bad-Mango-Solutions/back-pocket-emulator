// <copyright file="HostGamePortState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The last joystick, paddle, and button values the host accepted.
/// </summary>
/// <param name="Joystick0X">Joystick 0 X, from -1 to 1.</param>
/// <param name="Joystick0Y">Joystick 0 Y, from -1 to 1.</param>
/// <param name="Joystick1X">Joystick 1 X, from -1 to 1.</param>
/// <param name="Joystick1Y">Joystick 1 Y, from -1 to 1.</param>
/// <param name="Paddle0">Paddle 0. Joystick 0 X updates this.</param>
/// <param name="Paddle1">Paddle 1. Joystick 0 Y updates this.</param>
/// <param name="Paddle2">Paddle 2. Joystick 1 X updates this.</param>
/// <param name="Paddle3">Paddle 3. Joystick 1 Y updates this.</param>
/// <param name="Button0">Button 0.</param>
/// <param name="Button1">Button 1.</param>
/// <param name="Button2">Button 2.</param>
/// <param name="Button3">Button 3.</param>
public sealed record HostGamePortState(
    float Joystick0X,
    float Joystick0Y,
    float Joystick1X,
    float Joystick1Y,
    byte Paddle0,
    byte Paddle1,
    byte Paddle2,
    byte Paddle3,
    bool Button0,
    bool Button1,
    bool Button2,
    bool Button3);