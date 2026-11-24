// <copyright file="DiskInfoCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using System.Globalization;

using BadMango.Emulator.Devices;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Media;

/// <summary>
/// Reports format / geometry / write-protect / sniffed-order metadata for a disk image
/// without mounting it.
/// </summary>
/// <remarks>
/// <para>
/// Implements <c>disk info &lt;path&gt;</c>. Resolves only the
/// <see cref="Storage.Formats.DiskImageFactory"/> and <see cref="IDebugPathResolver"/>
/// from the supplied context — no machine is required.
/// </para>
/// <para>
/// For ambiguous <c>.dsk</c> images, also reports the sniffed sector ordering per
/// PRD §10 decision 5. For <c>.2mg</c>/<c>.2img</c> images, reports the parsed 2MG
/// header metadata (creator, format code, write-protect flag, embedded DOS volume).
/// </para>
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskInfoCommand : CommandHandlerBase, ICommandHelp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiskInfoCommand"/> class.
    /// </summary>
    public DiskInfoCommand()
        : base("disk-info", "Report format / geometry / metadata of a disk image without mounting it")
    {
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["diskinfo"];

    /// <inheritdoc/>
    public override string Usage => "disk-info <path>";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Reports the format DiskImageFactory would pick for the supplied image, the " +
        "geometry, the write-protect flag, whether the image is bootable, the sniffed " +
        ".dsk sector ordering (per PRD §10 decision 5) and any 2MG header metadata. The " +
        "image is opened read-only and is not mounted on any controller.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk-info game.dsk",
        "disk-info hd32m.hdv",
        "disk-info wrapped.2mg",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "None. The image is opened read-only and not mounted.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["disk", "disk-create"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return CommandResult.Error("Path required. Usage: disk-info <path>");
        }

        if (args.Length > 1)
        {
            return CommandResult.Error("Too many arguments. Usage: disk-info <path>");
        }

        var factory = (context as IDebugContext)?.DiskImageFactory;
        if (factory is null)
        {
            return CommandResult.Error("DiskImageFactory not available on debug context.");
        }

        var rawPath = args[0];
        string path = rawPath;
        var resolver = (context as IDebugContext)?.PathResolver;
        if (resolver is not null)
        {
            if (!resolver.TryResolve(rawPath, out var resolved))
            {
                return CommandResult.Error($"Cannot resolve path: '{rawPath}'.");
            }

            path = resolved!;
        }

        if (!File.Exists(path))
        {
            return CommandResult.Error($"File not found: '{path}'.");
        }

        var mounted = false;
        var mountedSlot = 0;
        var mountedDriveIndex = 0;
        DiskImageOpenResult? mountedOpen = null;
        if (context is IDebugContext mountedContext
            && mountedContext.MountedDisks.TryGetByPath(path, out mountedSlot, out mountedDriveIndex, out mountedOpen)
            && mountedOpen is not null)
        {
            mounted = true;
        }

        // Read the 2MG header (if any) before opening an unmounted image. Mounted 2MG
        // images are reported from the retained open result instead.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        TwoImgHeader? twoImgHeader = null;
        if (!mounted && ext is ".2mg" or ".2img")
        {
            try
            {
                var head = ReadFirstBytes(path, 64);
                if (head.Length >= 64 && head.AsSpan(0, 4).SequenceEqual(TwoImgHeader.Magic))
                {
                    twoImgHeader = TwoImgHeader.Parse(head);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return CommandResult.Error($"Cannot read '{path}': {ex.Message}");
            }
        }

        DiskImageOpenResult open;
        if (mountedOpen is not null)
        {
            open = mountedOpen;
        }
        else
        {
            try
            {
                open = factory.Open(path, forceReadOnly: true);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidDataException
                                       or NotSupportedException)
            {
                return CommandResult.Error($"Cannot identify '{path}': {ex.Message}");
            }
        }

        try
        {
            // Effective write-protect: OS read-only attribute, the mount-time read-only state
            // when the image is mounted, OR the 2MG header bit if present.
            var fileInfo = new FileInfo(path);
            var osReadOnly = (fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            var effectiveWriteProtected = osReadOnly
                || (mountedOpen is not null && open.IsReadOnly)
                || (twoImgHeader is TwoImgHeader hdr && hdr.IsWriteProtected);

            var output = context.Output;
            output.WriteLine();
            output.WriteLine($"Disk image: {path}");
            if (mounted)
            {
                output.WriteLine($"  Mounted:          yes (slot {mountedSlot}, drive {mountedDriveIndex + 1})");
                var unflushedChanges = "unknown";
                if (DiskRuntimeHelpers.TryGetSlotManager(context, out var slotManager, out _)
                    && slotManager is not null
                    && DiskRuntimeHelpers.TryGetController(slotManager, mountedSlot, mountedDriveIndex + 1, out var controller, out var driveIndex, out _)
                    && controller is not null)
                {
                    var snapshot = controller.GetDriveSnapshot(driveIndex);
                    unflushedChanges = snapshot.UnflushedChanges ? "yes" : "no";
                }

                output.WriteLine($"  Unflushed changes: {unflushedChanges}");
            }

            output.WriteLine($"  File size:        {fileInfo.Length} bytes");
            output.WriteLine($"  Detected format:  {open.Format}");
            output.WriteLine($"  Write-protected:  {(effectiveWriteProtected ? "yes" : "no")}");
            output.WriteLine($"  Bootable:         {DescribeBootability(open, context.Error)}");

            switch (open)
            {
                case Image525AndBlockResult both:
                    {
                        var geom = both.TrackMedia.Geometry;
                        var volumeName = TryReadProDosVolumeName(both.BlockMedia, open.Format, context.Error);
                        output.WriteLine($"  Media kind:       5.25\" sector image (track + block views)");
                        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Geometry:         {0} tracks x {1} sectors x {2} bytes/sector", geom.TrackCount, geom.SectorsPerTrack, geom.BytesPerSector));
                        output.WriteLine($"  Sector order:     {both.SectorOrder}");
                        output.WriteLine($"  Block count:      {both.BlockMedia.BlockCount} (512-byte blocks)");
                        if (Path.GetExtension(path).Equals(".dsk", StringComparison.OrdinalIgnoreCase))
                        {
                            output.WriteLine($"  .dsk sniffed:     {(both.WasOrderSniffed ? "yes (matched signature)" : "no (fell back to DOS)")}");
                        }

                        if (volumeName is not null)
                        {
                            output.WriteLine($"  Volume name:      {volumeName}");
                        }

                        break;
                    }

                case Image525Result trackOnly:
                    {
                        var geom = trackOnly.Media.Geometry;
                        output.WriteLine($"  Media kind:       5.25\" nibble image (track view only)");
                        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Geometry:         {0} tracks", geom.TrackCount));
                        break;
                    }

                case ImageBlockResult blockOnly:
                    {
                        var volumeName = TryReadProDosVolumeName(blockOnly.Media, open.Format, context.Error);
                        output.WriteLine($"  Media kind:       {DescribeBlockMediaKind(blockOnly.Media.BlockCount)}");
                        output.WriteLine($"  Block count:      {blockOnly.Media.BlockCount} ({blockOnly.Media.BlockSize}-byte blocks)");
                        if (volumeName is not null)
                        {
                            output.WriteLine($"  Volume name:      {volumeName}");
                        }

                        break;
                    }
            }

            // For 2MG images, report header metadata too.
            if (twoImgHeader is TwoImgHeader header)
            {
                output.WriteLine($"  2MG creator:      {header.Creator}");
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  2MG header len:   {0} bytes", header.HeaderLength));
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  2MG payload:      offset {0}, length {1}", header.DataOffset, header.DataLength));
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  2MG format code:  {0} ({1})", header.Format, FormatCodeName(header.Format)));
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  2MG flags:        0x{0:X8}", header.Flags));
                output.WriteLine($"  2MG write-prot:   {(header.IsWriteProtected ? "yes" : "no")}");
                if (header.HasDosVolumeNumber)
                {
                    output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  2MG DOS volume:   {0}", header.DosVolumeNumber));
                }
            }

            output.WriteLine();
            return CommandResult.Ok();
        }
        finally
        {
            if (!mounted)
            {
                open.Dispose();
            }
        }
    }

    private static byte[] ReadFirstBytes(string path, int count)
    {
        // Use FileShare.ReadWrite so this read can coexist with another open handle.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = (int)Math.Min(count, fs.Length);
        var buf = new byte[len];
        var read = 0;
        while (read < len)
        {
            var n = fs.Read(buf.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return buf;
    }

    private static string FormatCodeName(int code) => code switch
    {
        0 => "DOS 3.3 sector order",
        1 => "ProDOS sector order",
        2 => "nibble",
        _ => "unknown",
    };

    /// <summary>
    /// Returns a human-readable description of a pure block image based on its block count
    /// (e.g. an 800K 3.5" floppy is reported as such rather than as a generic "block image").
    /// </summary>
    private static string DescribeBlockMediaKind(int blockCount) => blockCount switch
    {
        1600 => "3.5\" block image (800K)",
        _ => "block image",
    };

    /// <summary>
    /// Returns a human-readable description of whether <paramref name="open"/> is bootable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both Disk II (5.25") and SmartPort (3.5" / hard disk) bootstrap by loading the first
    /// sector / block of the medium to <c>$0800</c> and jumping to <c>$0801</c>. The Disk II
    /// <c>$C600</c> controller ROM does this unconditionally — it does not itself inspect
    /// the loaded sector — so execution starts at the disk-supplied loader code at
    /// <c>$0801</c>. We therefore identify bootability by matching known Apple boot1
    /// signatures in block 0:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// ProDOS 8 (<i>Beneath Apple ProDOS</i>, ch. 2; Apple II Tech Note ProDOS #21,
    /// "Boot Block Format"): block 0 (the PBOOT loader) begins with
    /// <see cref="ProdosLayout.BootBlock.Signature"/>, matched at block offset 0.
    /// </description></item>
    /// <item><description>
    /// Apple DOS 3.3 (<i>Beneath Apple DOS</i>, Worth &amp; Lechner, App. C): block 0 begins
    /// with <see cref="Dos33Layout.BootSector.Signature"/>, where execution at <c>$0801</c>
    /// starts with <c>$A5 $27 ...</c>; matched at block offset 0.
    /// </description></item>
    /// <item><description>
    /// Freshly-formatted media that remains all <c>$00</c>/<c>$FF</c> does not match either
    /// signature and is reported as not bootable.
    /// </description></item>
    /// </list>
    /// <para>
    /// This signature-based check intentionally avoids the previous
    /// "any non-zero/non-<c>$FF</c> first byte" heuristic, which could report arbitrary
    /// garbage as bootable.
    /// </para>
    /// <para>
    /// For sector- and block-image opens we read block 0 via <see cref="IBlockMedia"/>; the
    /// first 256 bytes of block 0 always correspond to physical track 0 / sector 0 of the
    /// medium regardless of on-disk sector ordering, so the same check applies uniformly to
    /// <c>.dsk</c>, <c>.do</c>, <c>.po</c>, <c>.2mg</c>, and <c>.hdv</c>. For nibble-only
    /// opens (<c>.nib</c>, <c>.woz</c>) we report <c>unknown (nibble image)</c> rather
    /// than spinning up a GCR decoder inside the debug command.
    /// </para>
    /// </remarks>
    private static string DescribeBootability(DiskImageOpenResult open, TextWriter errorLog)
    {
        IBlockMedia? blockMedia = open switch
        {
            Image525AndBlockResult both => both.BlockMedia,
            ImageBlockResult blockOnly => blockOnly.Media,
            _ => null,
        };

        if (blockMedia is null)
        {
            return "unknown (nibble image)";
        }

        if (blockMedia.BlockCount < 1 || blockMedia.BlockSize < 1)
        {
            return "unknown (no boot block)";
        }

        var block = new byte[blockMedia.BlockSize];
        try
        {
            blockMedia.ReadBlock(0, block);
        }
        catch (Exception ex)
        {
            // Best-effort metadata: any IBlockMedia implementation may surface its own
            // exception types. Log and report unknown rather than aborting 'disk info'.
            errorLog.WriteLine($"disk-info: could not read boot block: {ex.GetType().Name}: {ex.Message}");
            return "unknown (boot block unreadable)";
        }

        if (block.AsSpan().StartsWith(ProdosLayout.BootBlock.Signature))
        {
            return "yes";
        }

        if (block.AsSpan().StartsWith(Dos33Layout.BootSector.Signature))
        {
            return "yes";
        }

        return "no";
    }

    /// <summary>
    /// Attempts to read the ProDOS volume name from block 2 of <paramref name="media"/>.
    /// Returns the parsed name, or <see langword="null"/> if the block does not look like
    /// a valid ProDOS volume directory key block (or the read fails).
    /// </summary>
    /// <remarks>
    /// <para>
    /// For untyped containers (e.g. <c>.hdv</c>) we still attempt the read because a
    /// well-formed ProDOS volume header is self-identifying via its <c>0xF</c> storage type
    /// nibble. Any I/O failure is logged to <paramref name="errorLog"/> and treated as
    /// "no name available" so non-ProDOS images simply omit the line.
    /// </para>
    /// <para>
    /// Per the ProDOS technical reference, a volume name is 1..15 characters, the first
    /// must be a letter, and remaining characters are letters, digits, or <c>'.'</c>.
    /// On-disk bytes are always uppercase. ProDOS 8 Tech Note #8 stores the case-bit
    /// word at key-block <c>+$1A</c>, which is <see cref="ProdosLayout.VolumeHeader.CaseBitsOffset"/>
    /// of the volume header entry. When bit 15 is set, the corresponding characters are
    /// reported in lowercase. The creation time at entry <c>$1A</c> is not that word.
    /// </para>
    /// </remarks>
    private static string? TryReadProDosVolumeName(IBlockMedia media, DiskImageFormat format, TextWriter errorLog)
    {
        if (media.BlockCount < 3 || media.BlockSize != 512)
        {
            return null;
        }

        // Only attempt for formats that may carry a ProDOS volume directory.
        if (format != DiskImageFormat.ProDosSectorImage
            && format != DiskImageFormat.TwoImgProDos
            && format != DiskImageFormat.HdvBlockImage)
        {
            return null;
        }

        var keyBlock = new byte[512];
        try
        {
            media.ReadBlock(2, keyBlock);
        }
        catch (Exception ex)
        {
            // Best-effort optional metadata. Any IBlockMedia implementation may surface
            // its own exception types (IOException, ObjectDisposedException, etc.). Log
            // and continue rather than aborting 'disk info'.
            errorLog.WriteLine($"disk-info: could not read ProDOS volume directory key block: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        // Volume directory key block: prev_pointer == 0 at offset 0..1, storage_type
        // nibble 0xF at offset 4 high nibble, name length 1..15 in low nibble.
        if (keyBlock[0] != 0 || keyBlock[1] != 0)
        {
            return null;
        }

        var typeAndLen = keyBlock[4];
        if ((typeAndLen & 0xF0) != 0xF0)
        {
            return null;
        }

        var nameLen = typeAndLen & 0x0F;
        if (nameLen is < 1 or > 15)
        {
            return null;
        }

        // ProDOS 8 Tech Note #8. The note's +$1A/+$1B counts from the start of the key
        // block. The header entry starts at offset 4, so the word is entry offset $16.
        // Bit 15 set means bits 14..0 lowercase those characters. A clear high bit leaves
        // the on-disk bytes unchanged. Entry $1A is the creation time, not this word.
        var caseOffset = ProdosLayout.Directory.FirstEntryOffset + ProdosLayout.VolumeHeader.CaseBitsOffset;
        var caseField = (ushort)(keyBlock[caseOffset] | (keyBlock[caseOffset + 1] << 8));
        var caseBitsValid = (caseField & 0x8000) != 0;

        var name = new char[nameLen];
        for (var i = 0; i < nameLen; i++)
        {
            var c = (char)keyBlock[5 + i];
            if (!IsValidProDosVolumeNameChar(c, isFirst: i == 0))
            {
                return null;
            }

            if (caseBitsValid && c >= 'A' && c <= 'Z' && (caseField & (1 << (14 - i))) != 0)
            {
                c = (char)(c + ('a' - 'A'));
            }

            name[i] = c;
        }

        return new string(name);
    }

    /// <summary>
    /// Returns whether <paramref name="c"/> is a legal character at the given position of a
    /// ProDOS volume name. The first character must be an upper-case letter; subsequent
    /// characters may also be digits or <c>'.'</c>. Validation runs against the on-disk
    /// (uppercase) bytes; lowercase rendering is handled separately via the case-bit map
    /// at <see cref="ProdosLayout.VolumeHeader.CaseBitsOffset"/> of the volume header.
    /// </summary>
    private static bool IsValidProDosVolumeNameChar(char c, bool isFirst)
    {
        if (c >= 'A' && c <= 'Z')
        {
            return true;
        }

        if (isFirst)
        {
            return false;
        }

        return (c >= '0' && c <= '9') || c == '.';
    }
}