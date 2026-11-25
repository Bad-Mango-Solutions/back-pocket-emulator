// <copyright file="DiskIISectorWriteTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Signaling;
using BadMango.Emulator.Storage.Backends;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;
using BadMango.Emulator.Storage.Tests.FileSystems;
using BadMango.Unit.Components;

using Moq;

/// <summary>
/// Guest writes through <see cref="DiskIIController"/> must survive a flush into a DOS-order
/// sector image. The synthetic DOS images come from <see cref="Dos33ImageBuilder"/>; these
/// tests never open <c>disks/</c> or <c>library/</c>.
/// </summary>
/// <remarks>
/// The builder images contain no RWTS binary, so the tests drive Q6/Q7 with the cycle gaps
/// of DOS 3.3's write routines (WRITADR and WRITESEC): 40-cycle self-sync <c>$FF</c> bytes,
/// a 32-cycle address and data field, and the 31-cycle aux nibble loop. The controller sees
/// the same strobe-to-strobe deltas a 6502 produces, because both shift strobes are 4-cycle
/// <c>$C0EC</c> reads and the scheduler advances by the instructions between them.
/// </remarks>
[TestFixture]
public class DiskIISectorWriteTests
{
    private const byte Volume = 254;
    private const byte Drive2 = 0xEB;
    private const byte MotorOn = 0xE9;
    private const byte Q6L = 0xEC;
    private const byte Q6H = 0xED;
    private const byte Q7L = 0xEE;
    private const byte Q7H = 0xEF;

    // DOS 3.3 WRITESEC / WRITADR strobe-to-strobe cycles (instruction start to instruction start).
    private const int SyncCycles = 40;
    private const int FieldCycles = 32;
    private const int AuxCycles = 31;
    private const int SyncToPrologueCycles = 36;
    private const int PrologueToAuxCycles = 35;
    private const int AddressToDataSyncCycles = 67;
    private const int AddressMatchToFirstSyncCycles = 128;
    private const int Gap1Bytes = 48;
    private const int AddressFieldBytes = 14;
    private const int Gap2Bytes = 5;
    private const int DataFieldBytes = 349;

    /// <summary>
    /// A save rewrites the data field after the address epilogue. The new bytes must come
    /// back from the sector image and from <see cref="Dos33FileSystem"/>.
    /// </summary>
    /// <param name="trackCount">DOS-order image size. 36 covers the extra outer cylinder.</param>
    [TestCase(35)]
    [TestCase(36)]
    public void DataFieldRewrite_AfterAddressMatch_PersistsToDosCatalog(int trackCount)
    {
        var original = Fill(0x11);
        var updated = Fill(0x5A);
        updated[0] = (byte)'S';
        updated[1] = (byte)'A';
        updated[2] = (byte)'V';
        updated[3] = (byte)'E';
        var fixture = new Dos33ImageBuilder(trackCount, Volume)
            .AddFile("PARTY", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { original })
            .Build();
        var data = fixture.Entries[0].DataSectors[0];
        Assert.That(data.Allocated, Is.True);
        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);

        using var harness = WriteHarness.Open(trackCount, fixture.Image.ToArray());
        harness.SelectDrive2();
        harness.MotorOn();
        harness.SeekToTrack(data.Track);
        var address = harness.FindAddress(physical, Volume, data.Track, stopOnEpilogue: true);

        var (nibbles, intervals) = DataFieldWrite(updated, data.Track, physical);
        harness.WriteNibbles(nibbles, intervals);
        harness.FlushDrive2();

        AssertWrittenSector(harness, data.Track, physical, updated, address);
        var catalog = new Dos33FileSystem(new SectorImageSectorReader(harness.Image), Generator.Log().Object);
        var raw = catalog.TryReadFile("PARTY", FileReadView.Raw);
        var logical = catalog.TryReadFile("PARTY", FileReadView.Logical);
        Assert.Multiple(() =>
        {
            Assert.That(raw.Succeeded, Is.True, raw.Reason);
            Assert.That(raw.Data.ToArray(), Is.EqualTo(updated));
            Assert.That(logical.Succeeded, Is.True, logical.Reason);
            Assert.That(logical.Data.ToArray(), Is.EqualTo(updated));
            Assert.That(harness.Drive1Bytes, Is.EqualTo(harness.Drive1Original));
        });
    }

    /// <summary>
    /// Formatting a sector writes 10-bit sync bytes, the address field, the gap, and the
    /// 6-and-2 data field. Drive 2 is at quarter-track 80, the head position from the
    /// save that did not stick.
    /// </summary>
    /// <param name="trackCount">DOS-order image size.</param>
    [TestCase(35)]
    [TestCase(36)]
    public void AddressGapAndDataWrite_AtQuarterTrack80_Persists(int trackCount)
    {
        const int track = 20;
        var updated = Fill(0x3C);
        updated[0] = (byte)'T';
        updated[1] = 20;
        using var harness = WriteHarness.Open(trackCount, Blank(trackCount));
        harness.SelectDrive2();
        harness.MotorOn();
        harness.SeekToTrack(track);
        Assert.That(harness.QuarterTrack, Is.EqualTo(80));

        WriteWholeSector(harness, track, physical: 0, updated);
        AssertSector(harness, track, physical: 0, updated);
    }

    /// <summary>
    /// The 36th track (147456-byte images) is a real cylinder, and a write there flushes.
    /// </summary>
    [Test]
    public void AddressGapAndDataWrite_On36thTrack_Persists()
    {
        const int trackCount = 36;
        const int track = 35;
        var updated = Fill(0x7E);
        updated[0] = (byte)'T';
        updated[1] = 35;
        using var harness = WriteHarness.Open(trackCount, Blank(trackCount));
        harness.SelectDrive2();
        harness.MotorOn();
        harness.SeekToTrack(track);
        Assert.That(harness.QuarterTrack, Is.EqualTo(track * 4));

        WriteWholeSector(harness, track, physical: 0, updated);
        AssertSector(harness, track, physical: 0, updated);
    }

    private static void WriteWholeSector(WriteHarness harness, int track, int physical, byte[] updated)
    {
        var address = harness.FindAddress(physical, Volume, track, stopOnEpilogue: false);

        // Head is on the checksum low byte, 10 bytes after the address prologue.
        // One revolution minus those 10 bytes and the 48-byte gap lands on the first
        // gap byte, so the write replaces this sector's slot in place.
        var walk = GcrEncoder.StandardTrackLength - 10 - Gap1Bytes;
        for (var i = 0; i < walk; i++)
        {
            harness.ReadFresh();
        }

        Assert.That(harness.LastNibble, Is.EqualTo(GcrEncoder.GapByte), "Sector gap before the address field is self-sync.");
        var (nibbles, intervals) = SectorSlotWrite(updated, track, physical);
        harness.WriteNibbles(nibbles, intervals);
        harness.FlushDrive2();
        Assert.That(address, Is.GreaterThanOrEqualTo(0));
    }

    private static void AssertSector(WriteHarness harness, int track, int physical, byte[] expected)
    {
        var actual = new byte[expected.Length];
        harness.Image.ReadSectorPhysical(track, physical, actual);
        var captured = harness.Captured(track * 4);
        var decoded = new byte[SectorSkew.SectorsPerTrack * GcrEncoder.BytesPerSector];
        var mask = captured is null ? 0 : GcrEncoder.DecodeTrack(captured, decoded);
        Assert.Multiple(() =>
        {
            Assert.That(mask & (1 << physical), Is.Not.Zero, $"Flushed nibbles did not decode sector {physical} (mask ${mask:X4}).");
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(harness.Drive1Bytes, Is.EqualTo(harness.Drive1Original));
        });
    }

    private static void AssertWrittenSector(WriteHarness harness, int track, int physical, byte[] expected, int addressIndex)
    {
        Assert.That(addressIndex, Is.GreaterThanOrEqualTo(0));
        AssertSector(harness, track, physical, expected);
    }

    private static byte[] Blank(int trackCount) => new Dos33ImageBuilder(trackCount, Volume).Build().Image.ToArray();

    private static byte[] Fill(byte value)
    {
        var data = new byte[GcrEncoder.BytesPerSector];
        Array.Fill(data, value);
        return data;
    }

    private static (byte[] Nibbles, int[] Intervals) DataFieldWrite(byte[] sector, int track, int physical)
    {
        var encoded = Encode(sector, track, physical, out var stride);
        var nibbles = new byte[Gap2Bytes + DataFieldBytes];
        nibbles.AsSpan(0, Gap2Bytes).Fill(GcrEncoder.GapByte);
        encoded.AsSpan(stride + Gap1Bytes + AddressFieldBytes + Gap2Bytes, DataFieldBytes).CopyTo(nibbles.AsSpan(Gap2Bytes));
        return (nibbles, DataFieldIntervals());
    }

    private static (byte[] Nibbles, int[] Intervals) SectorSlotWrite(byte[] sector, int track, int physical)
    {
        var encoded = Encode(sector, track, physical, out var stride);
        var nibbles = encoded.AsSpan(stride, GcrEncoder.EncodedSectorStride).ToArray();
        Assert.That(nibbles[Gap1Bytes], Is.EqualTo(GcrEncoder.AddressPrologue1));
        Assert.That(nibbles[Gap1Bytes + AddressFieldBytes + Gap2Bytes + 2], Is.EqualTo(GcrEncoder.DataPrologue3));
        return (nibbles, SectorSlotIntervals());
    }

    private static byte[] Encode(byte[] sector, int track, int physical, out int stride)
    {
        var sectors = new byte[SectorSkew.SectorsPerTrack * GcrEncoder.BytesPerSector];
        sector.CopyTo(sectors.AsSpan(physical * GcrEncoder.BytesPerSector));
        var encoded = new byte[GcrEncoder.StandardTrackLength];
        GcrEncoder.EncodeTrack(Volume, track, sectors, encoded);
        stride = physical * GcrEncoder.EncodedSectorStride;
        return encoded;
    }

    /// <summary>
    /// Cycles before each byte of a WRITESEC data-field write, including the five sync bytes.
    /// </summary>
    private static int[] DataFieldIntervals()
    {
        var intervals = new int[Gap2Bytes + DataFieldBytes];
        var n = 0;
        intervals[n++] = AddressMatchToFirstSyncCycles;
        for (var i = 0; i < Gap2Bytes - 1; i++)
        {
            intervals[n++] = SyncCycles;
        }

        intervals[n++] = SyncToPrologueCycles;
        intervals[n++] = FieldCycles;
        intervals[n++] = FieldCycles;
        intervals[n++] = PrologueToAuxCycles;
        for (var i = 0; i < 85; i++)
        {
            intervals[n++] = AuxCycles;
        }

        for (var i = 0; i < 256 + 1 + 3; i++)
        {
            intervals[n++] = FieldCycles;
        }

        Assert.That(n, Is.EqualTo(intervals.Length));
        return intervals;
    }

    private static int[] SectorSlotIntervals()
    {
        var intervals = new int[GcrEncoder.EncodedSectorStride];
        var n = 0;
        intervals[n++] = 0;
        for (var i = 0; i < Gap1Bytes - 1; i++)
        {
            intervals[n++] = SyncCycles;
        }

        for (var i = 0; i < AddressFieldBytes; i++)
        {
            intervals[n++] = FieldCycles;
        }

        intervals[n++] = AddressToDataSyncCycles;
        for (var i = 0; i < Gap2Bytes - 1; i++)
        {
            intervals[n++] = SyncCycles;
        }

        var data = DataFieldIntervals();
        for (var i = Gap2Bytes; i < data.Length; i++)
        {
            intervals[n++] = data[i];
        }

        Assert.That(n, Is.EqualTo(intervals.Length));
        return intervals;
    }

    private static byte Decode44(byte high, byte low) => (byte)(((high << 1) | 1) & low);

    /// <summary>
    /// Slot 6, two drives, settle delays disabled so the write itself is what is under test.
    /// Drive 1 holds a 36-track image and is not written.
    /// </summary>
    private sealed class WriteHarness : IDisposable
    {
        private readonly DiskIIController controller;
        private readonly IOPageDispatcher dispatcher;
        private readonly Scheduler scheduler;
        private readonly BusAccess access;
        private readonly CapturingMedia capture;
        private readonly byte[] drive1Original;
        private readonly RamStorageBackend drive1Backend;
        private readonly RamStorageBackend drive2Backend;
        private int bytesRead;

        private WriteHarness(
            DiskIIController controller,
            IOPageDispatcher dispatcher,
            Scheduler scheduler,
            BusAccess access,
            SectorImageMedia image,
            CapturingMedia capture,
            byte[] drive1Original,
            RamStorageBackend drive1Backend,
            RamStorageBackend drive2Backend)
        {
            this.controller = controller;
            this.dispatcher = dispatcher;
            this.scheduler = scheduler;
            this.access = access;
            this.Image = image;
            this.capture = capture;
            this.drive1Original = drive1Original;
            this.drive1Backend = drive1Backend;
            this.drive2Backend = drive2Backend;
        }

        public SectorImageMedia Image { get; }

        public int QuarterTrack => this.controller.GetDriveSnapshot(1).QuarterTrack;

        public byte LastNibble { get; private set; }

        public byte[] Drive1Original => this.drive1Original;

        public byte[] Drive1Bytes => this.drive1Backend.ToArray();

        public static WriteHarness Open(int trackCount, byte[] drive2Image)
        {
            var controller = new DiskIIController(
                Generator.Log().Object,
                motorSettleCycles: 0,
                trackStepSettleCycles: 0,
                motorOffDelayCycles: 0);
            controller.SlotNumber = 6;
            var dispatcher = new IOPageDispatcher();
            dispatcher.InstallSlotHandlers(6, controller.IOHandlers!);
            var scheduler = new Scheduler();
            var bus = new Mock<IMemoryBus>();
            var signals = new Mock<ISignalBus>();
            var context = new EventContext(scheduler, signals.Object, bus.Object);
            scheduler.SetEventContext(context);
            controller.Initialize(context);

            var drive1Original = new Dos33ImageBuilder(36, Volume).Build().Image.ToArray();
            var drive1Backend = new RamStorageBackend(drive1Original);
            var drive1 = new SectorImageMedia(drive1Backend, new DiskGeometry(36, 16, 256, SectorOrder.Dos33), volume: Volume);
            var drive2Backend = new RamStorageBackend(drive2Image);
            var image = new SectorImageMedia(
                drive2Backend,
                new DiskGeometry(trackCount, 16, 256, SectorOrder.Dos33),
                volume: Volume);
            var capture = new CapturingMedia(image.As525Media());
            controller.Mount(0, drive1.As525Media());
            controller.Mount(1, capture);
            var access = new BusAccess(
                Address: 0xC0E0,
                Value: 0,
                WidthBits: 8,
                Mode: BusAccessMode.Decomposed,
                EmulationFlag: true,
                Intent: AccessIntent.DataRead,
                SourceId: 0,
                Cycle: 0,
                Flags: AccessFlags.None);
            return new WriteHarness(controller, dispatcher, scheduler, access, image, capture, drive1Original, drive1Backend, drive2Backend);
        }

        public void SelectDrive2() => this.Read(Drive2);

        public void MotorOn() => this.Read(DiskIISectorWriteTests.MotorOn);

        public void SeekToTrack(int track)
        {
            var target = track * 4;
            while (this.QuarterTrack < target)
            {
                this.StepHalf(towardZero: false);
            }

            while (this.QuarterTrack > target)
            {
                this.StepHalf(towardZero: true);
            }
        }

        public byte ReadFresh()
        {
            if (this.bytesRead > 0)
            {
                this.scheduler.Advance(new Cycle(DiskIIController.CyclesPerByte));
            }

            var value = this.Read(Q6L);
            Assert.That(value & 0x80, Is.EqualTo(0x80), $"Nibble {this.bytesRead} was not byte-ready.");
            this.bytesRead++;
            this.LastNibble = value;
            return value;
        }

        public int FindAddress(int physical, int volume, int track, bool stopOnEpilogue)
        {
            var state = 0;
            var d5Index = 0;
            Span<byte> header = stackalloc byte[8];
            var filled = 0;
            var guard = (GcrEncoder.StandardTrackLength * 2) + 32;
            for (var n = 0; n < guard; n++)
            {
                var value = this.ReadFresh();
                if (state == 0)
                {
                    if (value == GcrEncoder.AddressPrologue1)
                    {
                        state = 1;
                        d5Index = this.bytesRead - 1;
                    }

                    continue;
                }

                if (state == 1)
                {
                    if (value == GcrEncoder.AddressPrologue2)
                    {
                        state = 2;
                    }
                    else if (value == GcrEncoder.AddressPrologue1)
                    {
                        d5Index = this.bytesRead - 1;
                    }
                    else
                    {
                        state = 0;
                    }

                    continue;
                }

                if (state == 2)
                {
                    if (value == GcrEncoder.AddressPrologue3)
                    {
                        state = 3;
                        filled = 0;
                    }
                    else if (value == GcrEncoder.AddressPrologue1)
                    {
                        state = 1;
                        d5Index = this.bytesRead - 1;
                    }
                    else
                    {
                        state = 0;
                    }

                    continue;
                }

                header[filled++] = value;
                if (filled < 8)
                {
                    continue;
                }

                state = 0;
                var vol = Decode44(header[0], header[1]);
                var trk = Decode44(header[2], header[3]);
                var sec = Decode44(header[4], header[5]);
                var chk = Decode44(header[6], header[7]);
                if ((vol ^ trk ^ sec) != chk || sec != physical || trk != track || vol != volume)
                {
                    continue;
                }

                if (stopOnEpilogue)
                {
                    Assert.That(this.ReadFresh(), Is.EqualTo(GcrEncoder.EpiloguePrologue1));
                    Assert.That(this.ReadFresh(), Is.EqualTo(GcrEncoder.EpiloguePrologue2));
                }

                return d5Index;
            }

            Assert.Fail($"No address field for track {track} physical sector {physical}.");
            return -1;
        }

        public void WriteNibbles(ReadOnlySpan<byte> nibbles, ReadOnlySpan<int> intervals)
        {
            Assert.That(intervals.Length, Is.EqualTo(nibbles.Length));
            this.scheduler.Advance(new Cycle((ulong)intervals[0]));
            this.Read(Q6H);
            this.Read(Q7L);
            this.Write(Q7H, nibbles[0]);
            this.Read(Q6L);
            for (var i = 1; i < nibbles.Length; i++)
            {
                this.scheduler.Advance(new Cycle((ulong)intervals[i]));
                this.Write(Q6H, nibbles[i]);
                this.Read(Q6L);
            }

            this.Read(Q7L);
        }

        public void FlushDrive2() => this.controller.Flush(1);

        public byte[]? Captured(int quarterTrack) => this.capture.Written.TryGetValue(quarterTrack, out var nibbles) ? nibbles : null;

        public void Dispose()
        {
            this.drive1Backend.Dispose();
            this.drive2Backend.Dispose();
        }

        private void StepHalf(bool towardZero)
        {
            var current = (this.QuarterTrack >> 1) & 3;
            var next = (current + (towardZero ? 3 : 1)) & 3;
            this.Read((byte)(0xE0 + (next * 2) + 1));
            this.Read((byte)(0xE0 + (current * 2)));
        }

        private byte Read(byte offset)
        {
            var busAccess = this.access;
            return this.dispatcher.Read(offset, in busAccess);
        }

        private void Write(byte offset, byte value)
        {
            var busAccess = this.access with { Intent = AccessIntent.DataWrite };
            this.dispatcher.Write(offset, value, in busAccess);
        }
    }

    private sealed class CapturingMedia : I525Media
    {
        private readonly I525Media inner;

        public CapturingMedia(I525Media inner)
        {
            this.inner = inner;
        }

        public Dictionary<int, byte[]> Written { get; } = new();

        public DiskGeometry Geometry => this.inner.Geometry;

        public int OptimalTrackLength => this.inner.OptimalTrackLength;

        public bool IsReadOnly => this.inner.IsReadOnly;

        public void ReadTrack(int quarterTrack, Span<byte> destination) => this.inner.ReadTrack(quarterTrack, destination);

        public void WriteTrack(int quarterTrack, ReadOnlySpan<byte> source)
        {
            this.Written[quarterTrack] = source.ToArray();
            this.inner.WriteTrack(quarterTrack, source);
        }

        public void Flush() => this.inner.Flush();
    }
}