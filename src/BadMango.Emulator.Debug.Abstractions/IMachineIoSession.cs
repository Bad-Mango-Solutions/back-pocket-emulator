// <copyright file="IMachineIoSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Defines machine I/O helpers for screen, keyboard, and removable-disk workflows.
/// </summary>
public interface IMachineIoSession
{
    /// <summary>
    /// Gets the current text-screen snapshot from the machine's video/text model.
    /// </summary>
    /// <returns>The current text-screen snapshot.</returns>
    ScreenTextSnapshot GetScreenText();

    /// <summary>
    /// Gets the current video soft-switch state.
    /// </summary>
    /// <returns>The current video state.</returns>
    VideoStateSnapshot GetVideoState();

    /// <summary>
    /// Queues keys for typed input.
    /// </summary>
    /// <param name="text">The text to queue.</param>
    /// <returns>A receipt describing accepted and skipped characters.</returns>
    TypeKeysReceipt TypeKeys(string text);

    /// <summary>
    /// Mounts a disk image into the selected removable drive.
    /// </summary>
    /// <param name="slot">The slot number (1-7).</param>
    /// <param name="drive">The drive number (1-based).</param>
    /// <param name="path">The image path (literal or resolver-supported scheme).</param>
    /// <param name="writeProtect">Whether to open the image read-only.</param>
    void MountDisk(int slot, int drive, string path, bool writeProtect = false);

    /// <summary>
    /// Ejects a mounted disk from the selected removable drive.
    /// </summary>
    /// <param name="slot">The slot number (1-7).</param>
    /// <param name="drive">The drive number (1-based).</param>
    void EjectDisk(int slot, int drive);

    /// <summary>
    /// Lists installed disk controllers and current per-drive snapshot data.
    /// </summary>
    /// <returns>The live controller and drive list.</returns>
    IReadOnlyList<DiskControllerSnapshot> ListDisks();

    /// <summary>
    /// Steps execution until the text screen matches a pattern, or budget is exhausted.
    /// </summary>
    /// <param name="regexPattern">The regular-expression pattern to match.</param>
    /// <param name="budget">The execution budget.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the pattern matched before budget exhaustion.</returns>
    bool WaitForScreenText(string regexPattern, RunBudget budget, CancellationToken cancellationToken = default);
}