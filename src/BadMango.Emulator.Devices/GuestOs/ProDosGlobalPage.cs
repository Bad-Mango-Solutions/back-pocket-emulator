// <copyright file="ProDosGlobalPage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// ProDOS 8 global-page addresses and the one validation of that page.
/// </summary>
/// <remarks>
/// <para>
/// TechRef §5.2.4, ProDOS 8 TN #23, and the cc65 <c>dosdetect.s</c> check are
/// implemented here. The slotless clock and the ProDOS guest-OS probe both call
/// <see cref="Inspect"/>. They do not keep a second copy of the rules.
/// </para>
/// <para>
/// <c>KVERSION</c> labels: <c>$00</c> 1.0.x, <c>$01</c> 1.1.x, <c>$02</c>–<c>$07</c>
/// 1.2–1.7, <c>$08</c> 1.8/1.9 (both use <c>$08</c>), <c>$21</c> 2.0.1, <c>$22</c> 2.0.2,
/// <c>$23</c> 2.0.3, <c>$24</c> 2.4 through 2.4.3 (labeled "2.4.x").
/// </para>
/// </remarks>
public static class ProDosGlobalPage
{
    /// <summary>MLI <c>ENTRY</c> opcode. ProDOS stores <c>JMP</c>.</summary>
    public const byte Jmp = 0x4C;

    /// <summary><c>RTS</c>. ProDOS stores this at <c>DATETIME</c> when no clock driver is installed.</summary>
    public const byte Rts = 0x60;

    /// <summary><c>MACHID</c> bit 0: a recognizable clock.</summary>
    public const byte ClockBit = 0x01;

    /// <summary>MLI <c>ENTRY</c> at <c>$BF00</c>.</summary>
    public const ushort MliEntry = 0xBF00;

    /// <summary><c>DATETIME</c> opcode at <c>$BF06</c>.</summary>
    public const ushort DateTime = 0xBF06;

    /// <summary>Little-endian <c>DATETIME</c> vector at <c>$BF07</c>.</summary>
    public const ushort DateTimeVector = 0xBF07;

    /// <summary><c>MACHID</c> at <c>$BF98</c>.</summary>
    public const ushort MachIdAddress = 0xBF98;

    /// <summary><c>IVERSION</c> at <c>$BFFD</c>.</summary>
    public const ushort IVersionAddress = 0xBFFD;

    /// <summary><c>KVERSION</c> at <c>$BFFF</c>.</summary>
    public const ushort KVersionAddress = 0xBFFF;

    /// <summary>First address of the language-card / ROM window. The clock driver must point here.</summary>
    public const ushort LanguageCardStart = 0xD000;

    /// <summary>
    /// Reads the global page and reports which ProDOS 8 checks passed.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <returns>The checks. <see cref="Inspection.IsKnownProDos8"/> is the single "this is ProDOS 8" result.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static Inspection Inspect(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var entry = memory.Read(MliEntry);
        var dateTimeOpcode = memory.Read(DateTime);
        var driver = ReadWord(memory, DateTimeVector);
        var kVersion = memory.Read(KVersionAddress);
        return new Inspection(
            entry == Jmp,
            dateTimeOpcode is Jmp or Rts,
            driver >= LanguageCardStart,
            driver,
            dateTimeOpcode,
            kVersion,
            memory.Read(IVersionAddress),
            memory.Read(MachIdAddress),
            VersionLabelFor(kVersion));
    }

    /// <summary>
    /// Maps a known <c>KVERSION</c> byte onto its label.
    /// </summary>
    /// <param name="kVersion">The byte at <c>$BFFF</c>.</param>
    /// <returns>The label, or <see langword="null"/> when the byte is not a ProDOS 8 version through 2.4.3.</returns>
    public static string? VersionLabelFor(byte kVersion) => kVersion switch
    {
        0x00 => "ProDOS 8 1.0.x",
        0x01 => "ProDOS 8 1.1.x",
        0x02 => "ProDOS 8 1.2",
        0x03 => "ProDOS 8 1.3",
        0x04 => "ProDOS 8 1.4",
        0x05 => "ProDOS 8 1.5",
        0x06 => "ProDOS 8 1.6",
        0x07 => "ProDOS 8 1.7",
        0x08 => "ProDOS 8 1.8/1.9",
        0x21 => "ProDOS 8 2.0.1",
        0x22 => "ProDOS 8 2.0.2",
        0x23 => "ProDOS 8 2.0.3",
        0x24 => "ProDOS 8 2.4.x",
        _ => null,
    };

    private static ushort ReadWord(IGuestMemoryReader memory, ushort address)
    {
        var low = memory.Read(address);
        var high = memory.Read((ushort)(address + 1));
        return (ushort)(low | (high << 8));
    }

    /// <summary>One read of the ProDOS 8 global page.</summary>
    public readonly struct Inspection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Inspection"/> struct.
        /// </summary>
        /// <param name="hasMliJmp"><see langword="true"/> when <c>$BF00</c> is <c>JMP</c>.</param>
        /// <param name="hasDateTimeOpcode"><see langword="true"/> when <c>$BF06</c> is <c>RTS</c> or <c>JMP</c>.</param>
        /// <param name="driverInLanguageCard"><see langword="true"/> when <c>$BF07/$BF08</c> points at <c>$D000</c>–<c>$FFFF</c>.</param>
        /// <param name="driverAddress">The <c>DATETIME</c> vector.</param>
        /// <param name="dateTimeOpcode">The byte at <c>$BF06</c>.</param>
        /// <param name="kVersion">The byte at <c>$BFFF</c>.</param>
        /// <param name="iVersion">The byte at <c>$BFFD</c>.</param>
        /// <param name="machId">The byte at <c>$BF98</c>.</param>
        /// <param name="versionLabel">The label for <paramref name="kVersion"/>, or <see langword="null"/>.</param>
        public Inspection(
            bool hasMliJmp,
            bool hasDateTimeOpcode,
            bool driverInLanguageCard,
            ushort driverAddress,
            byte dateTimeOpcode,
            byte kVersion,
            byte iVersion,
            byte machId,
            string? versionLabel)
        {
            this.HasMliJmp = hasMliJmp;
            this.HasDateTimeOpcode = hasDateTimeOpcode;
            this.DriverInLanguageCard = driverInLanguageCard;
            this.DriverAddress = driverAddress;
            this.DateTimeOpcode = dateTimeOpcode;
            this.KVersion = kVersion;
            this.IVersion = iVersion;
            this.MachId = machId;
            this.VersionLabel = versionLabel;
        }

        /// <summary>Gets a value indicating whether <c>$BF00</c> is <c>JMP</c>.</summary>
        /// <value><see langword="true"/> when MLI <c>ENTRY</c> is present.</value>
        public bool HasMliJmp { get; }

        /// <summary>Gets a value indicating whether <c>$BF06</c> is <c>RTS</c> or <c>JMP</c>.</summary>
        /// <value><see langword="true"/> when the <c>DATETIME</c> opcode is one ProDOS writes.</value>
        public bool HasDateTimeOpcode { get; }

        /// <summary>Gets a value indicating whether the <c>DATETIME</c> vector points into <c>$D000</c>–<c>$FFFF</c>.</summary>
        /// <value><see langword="true"/> when the driver address is in the language card or ROM.</value>
        public bool DriverInLanguageCard { get; }

        /// <summary>Gets the <c>DATETIME</c> vector.</summary>
        /// <value>The little-endian word at <c>$BF07</c>.</value>
        public ushort DriverAddress { get; }

        /// <summary>Gets the <c>DATETIME</c> opcode.</summary>
        /// <value>The byte at <c>$BF06</c>.</value>
        public byte DateTimeOpcode { get; }

        /// <summary>Gets <c>KVERSION</c>.</summary>
        /// <value>The byte at <c>$BFFF</c>.</value>
        public byte KVersion { get; }

        /// <summary>Gets <c>IVERSION</c>.</summary>
        /// <value>The byte at <c>$BFFD</c>.</value>
        public byte IVersion { get; }

        /// <summary>Gets <c>MACHID</c>.</summary>
        /// <value>The byte at <c>$BF98</c>.</value>
        public byte MachId { get; }

        /// <summary>Gets the label for <see cref="KVersion"/>.</summary>
        /// <value>The ProDOS 8 label, or <see langword="null"/> when the byte is not a known version.</value>
        public string? VersionLabel { get; }

        /// <summary>Gets <see langword="true"/> when <c>MACHID</c> bit 0 is set.</summary>
        /// <value>The recognizable-clock flag. This is not the <c>DATETIME</c> opcode.</value>
        public bool HasClock => (this.MachId & ClockBit) != 0;

        /// <summary>Gets why the page is not a ProDOS global page, or <see langword="null"/> when the structure matches.</summary>
        /// <value>The first failing structural check, or <see langword="null"/>.</value>
        public string? StructuralRejection
        {
            get
            {
                if (!this.HasMliJmp)
                {
                    return "$BF00 is not a JMP.";
                }

                if (!this.HasDateTimeOpcode)
                {
                    return "$BF06 is not RTS or JMP.";
                }

                if (!this.DriverInLanguageCard)
                {
                    return "DATETIME vector is outside $D000-$FFFF.";
                }

                return null;
            }
        }

        /// <summary>Gets a value indicating whether this page is ProDOS 8 through 2.4.3.</summary>
        /// <value><see langword="true"/> when the structure matches and <see cref="VersionLabel"/> is set.</value>
        public bool IsKnownProDos8 => this.StructuralRejection is null && this.VersionLabel is not null;
    }
}