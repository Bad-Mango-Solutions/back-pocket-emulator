// <copyright file="VideoWriteEvent.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// One reserved video-region write. The log that would produce these is not installed in v1.
/// </summary>
/// <param name="CycleInFrame">The cycle of the write, counted from the frame window start.</param>
/// <param name="Address">The address that was written.</param>
/// <param name="Value">The byte that was written.</param>
/// <param name="Bank">The bank the write landed in.</param>
public readonly record struct VideoWriteEvent(uint CycleInFrame, uint Address, byte Value, VideoMemoryBank Bank);