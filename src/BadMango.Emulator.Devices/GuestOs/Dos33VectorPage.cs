// <copyright file="Dos33VectorPage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// DOS 3.3 page-3 vector addresses and the check that they are <c>JMP</c>s.
/// </summary>
/// <remarks>
/// Beneath Apple DOS chapters 4 and 8. The 48K targets are warm start <c>$9DBF</c>,
/// cold start <c>$9D84</c>, file manager <c>$AAFD</c>, RWTS <c>$B7B5</c>, and
/// reconnect <c>$A851</c>. There is no in-memory DOS version byte; the label
/// comes from these vectors.
/// </remarks>
public static class Dos33VectorPage
{
    /// <summary><c>JMP</c> opcode required at each vector.</summary>
    public const byte Jmp = 0x4C;

    /// <summary>Warm-start vector at <c>$03D0</c>.</summary>
    public const ushort WarmStart = 0x03D0;

    /// <summary>48K warm-start target <c>$9DBF</c>.</summary>
    public const ushort WarmStartTarget = 0x9DBF;

    /// <summary>Cold-start vector at <c>$03D3</c>.</summary>
    public const ushort ColdStart = 0x03D3;

    /// <summary>48K cold-start target <c>$9D84</c>.</summary>
    public const ushort ColdStartTarget = 0x9D84;

    /// <summary>File-manager vector at <c>$03D6</c>.</summary>
    public const ushort FileManager = 0x03D6;

    /// <summary>48K file-manager target <c>$AAFD</c>.</summary>
    public const ushort FileManagerTarget = 0xAAFD;

    /// <summary>RWTS vector at <c>$03D9</c>.</summary>
    public const ushort Rwts = 0x03D9;

    /// <summary>48K RWTS target <c>$B7B5</c>.</summary>
    public const ushort RwtsTarget = 0xB7B5;

    /// <summary>Reconnect vector at <c>$03EA</c>.</summary>
    public const ushort Reconnect = 0x03EA;

    /// <summary>48K reconnect target <c>$A851</c>.</summary>
    public const ushort ReconnectTarget = 0xA851;

    /// <summary>Label when all five vectors are <c>JMP</c>s to the 48K targets.</summary>
    public const string StandardLabel = "DOS 3.3";

    /// <summary>Label when all five vectors are <c>JMP</c>s to other targets.</summary>
    public const string NonstandardLabel = "DOS 3.3-compatible (nonstandard vectors)";

    /// <summary>
    /// Reads the five page-3 vectors.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <returns>Whether each entry is a <c>JMP</c> and whether the targets are the 48K addresses.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static Inspection Inspect(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (!TryReadJmp(memory, WarmStart, WarmStartTarget, out var warmMatches))
        {
            return new Inspection(false, false, WarmStart);
        }

        if (!TryReadJmp(memory, ColdStart, ColdStartTarget, out var coldMatches))
        {
            return new Inspection(false, false, ColdStart);
        }

        if (!TryReadJmp(memory, FileManager, FileManagerTarget, out var fileManagerMatches))
        {
            return new Inspection(false, false, FileManager);
        }

        if (!TryReadJmp(memory, Rwts, RwtsTarget, out var rwtsMatches))
        {
            return new Inspection(false, false, Rwts);
        }

        if (!TryReadJmp(memory, Reconnect, ReconnectTarget, out var reconnectMatches))
        {
            return new Inspection(false, false, Reconnect);
        }

        var standard = warmMatches && coldMatches && fileManagerMatches && rwtsMatches && reconnectMatches;
        return new Inspection(true, standard, 0);
    }

    /// <summary>
    /// Returns the rejection text for a vector that is not a <c>JMP</c>.
    /// </summary>
    /// <param name="address">The vector address from <see cref="Inspection.MissingJmp"/>.</param>
    /// <returns>A non-empty reason.</returns>
    public static string MissingJmpReason(ushort address) => address switch
    {
        WarmStart => "$03D0 is not a JMP.",
        ColdStart => "$03D3 is not a JMP.",
        FileManager => "$03D6 is not a JMP.",
        Rwts => "$03D9 is not a JMP.",
        Reconnect => "$03EA is not a JMP.",
        _ => "A DOS 3.3 page 3 vector is not a JMP.",
    };

    private static bool TryReadJmp(IGuestMemoryReader memory, ushort address, ushort expected, out bool targetMatches)
    {
        if (memory.Read(address) != Jmp)
        {
            targetMatches = false;
            return false;
        }

        var target = (ushort)(memory.Read((ushort)(address + 1)) | (memory.Read((ushort)(address + 2)) << 8));
        targetMatches = target == expected;
        return true;
    }

    /// <summary>One read of the DOS 3.3 page-3 vectors.</summary>
    public readonly struct Inspection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Inspection"/> struct.
        /// </summary>
        /// <param name="allJumps"><see langword="true"/> when all five entries are <c>JMP</c>.</param>
        /// <param name="standardTargets"><see langword="true"/> when those <c>JMP</c>s use the 48K targets.</param>
        /// <param name="missingJmp">The first entry that is not a <c>JMP</c>, or 0 when <paramref name="allJumps"/> is <see langword="true"/>.</param>
        public Inspection(bool allJumps, bool standardTargets, ushort missingJmp)
        {
            this.AllJumps = allJumps;
            this.StandardTargets = standardTargets;
            this.MissingJmp = missingJmp;
        }

        /// <summary>Gets a value indicating whether all five vectors are <c>JMP</c>.</summary>
        /// <value><see langword="true"/> when each opcode is <c>$4C</c>.</value>
        public bool AllJumps { get; }

        /// <summary>Gets a value indicating whether the targets are the standard 48K addresses.</summary>
        /// <value><see langword="true"/> only when <see cref="AllJumps"/> is <see langword="true"/> and every target matches.</value>
        public bool StandardTargets { get; }

        /// <summary>Gets the first vector that is not a <c>JMP</c>.</summary>
        /// <value>The address, or 0 when <see cref="AllJumps"/> is <see langword="true"/>.</value>
        public ushort MissingJmp { get; }
    }
}