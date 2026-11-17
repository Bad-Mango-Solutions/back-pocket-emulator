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
    /// Unmounts the disk image from the selected drive.
    /// </summary>
    /// <param name="slot">The peripheral slot number (1-7).</param>
    /// <param name="drive">The drive number (1-based).</param>
    /// <remarks>
    /// <para>
    /// Today's controllers are Disk II drives. Unmount flushes pending writes, releases the
    /// image, and leaves the drive empty. A flush or write-back failure is reported and the
    /// image stays mounted. Eject signaling, eject-inhibit, and media lock belong to later
    /// removable drives and are not part of this operation.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The drive is empty, the slot has no disk controller, or pending writes could not be flushed.
    /// </exception>
    void EjectDisk(int slot, int drive);

    /// <summary>
    /// Lists installed disk controllers and current per-drive snapshot data.
    /// </summary>
    /// <returns>The live controller and drive list.</returns>
    IReadOnlyList<DiskControllerSnapshot> ListDisks();

    /// <summary>
    /// Steps execution until the text screen matches a pattern, or the wait stops for another reason.
    /// </summary>
    /// <param name="regexPattern">The regular-expression pattern to match. The match has a timeout.</param>
    /// <param name="budget">The execution budget.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A result whose <see cref="ScreenTextWaitResult.Reason"/> is <see cref="ScreenTextWaitReason.Matched"/>
    /// or the reason the wait stopped. Step failures are <see cref="ScreenTextWaitReason.Faulted"/>
    /// and keep the exception.
    /// </returns>
    /// <remarks>
    /// The screen is checked about once per frame (17,030 cycles), not after every instruction.
    /// </remarks>
    ScreenTextWaitResult WaitForScreenText(string regexPattern, RunBudget budget, CancellationToken cancellationToken = default);
}