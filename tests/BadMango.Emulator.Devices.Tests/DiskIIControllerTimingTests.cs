// <copyright file="DiskIIControllerTimingTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Signaling;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;
using BadMango.Unit.Components;

using Moq;

/// <summary>
/// Characterization of the RWTS-facing Disk II timing on <c>main</c> (issue #675).
/// The tests build their media in memory and do not load a boot ROM.
/// </summary>
[TestFixture]
public class DiskIIControllerTimingTests
{
    private const byte Volume = 0xFE;
    private const byte Q6L = 0xEC;
    private const byte MotorOn = 0xE9;
    private const int PollCycles = 4;

    /// <summary>
    /// Consecutive <c>LDA $C08C,X / BPL</c> polls that land on one spin position
    /// see bit 7 cleared, and the next byte-time serves the immediate successor nibble.
    /// </summary>
    [Test]
    public void RapidPolls_AtOneSpinPosition_ClearBit7AndDoNotAdvance()
    {
        var track = WriteTableTrack();
        var harness = new TimingHarness(new InMemoryTrackMedia(track), motorSettleCycles: 0);
        harness.ReadSwitch(MotorOn);

        var primed = harness.ReadSwitch(Q6L);
        var afterPrime = harness.Controller.GetActivitySnapshot();

        const int rapidPolls = 4;
        for (var i = 0; i < rapidPolls; i++)
        {
            harness.Advance(PollCycles);
            var poll = harness.ReadSwitch(Q6L);
            Assert.That(poll & 0x80, Is.EqualTo(0), "A poll inside the same spin position clears bit 7.");
            Assert.That(poll, Is.EqualTo((byte)(primed & 0x7F)));
        }

        var afterPolls = harness.Controller.GetActivitySnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(primed, Is.EqualTo(track[0]));
            Assert.That(primed & 0x80, Is.EqualTo(0x80));
            Assert.That(afterPolls.StaleByteCount - afterPrime.StaleByteCount, Is.EqualTo(rapidPolls));
            Assert.That(afterPolls.FreshByteCount, Is.EqualTo(afterPrime.FreshByteCount));
            Assert.That(harness.QuarterTrack, Is.EqualTo(0));
        });

        // Four 4-cycle polls consumed 16 cycles. One more 16 reaches exactly one byte time.
        harness.Advance(DiskIIController.CyclesPerByte - (rapidPolls * PollCycles));
        var successor = harness.ReadSwitch(Q6L);
        Assert.Multiple(() =>
        {
            Assert.That(successor, Is.EqualTo(track[1]), "The polls must not have moved the spin position.");
            Assert.That(successor & 0x80, Is.EqualTo(0x80));
        });
    }

    /// <summary>
    /// A tight poll while the head is settling after a track step sees bit 7 cleared.
    /// The next read after the settle window expires is a fresh nibble.
    /// </summary>
    [Test]
    public void TrackStepSettle_TightPollClearsBit7ThenFreshByte()
    {
        var track = WriteTableTrack();
        var harness = new TimingHarness(new InMemoryTrackMedia(track), motorSettleCycles: 0);
        harness.ReadSwitch(MotorOn);

        var primed = harness.ReadSwitch(Q6L);
        Assert.That(primed, Is.EqualTo(track[0]));

        // Phase 1 on from phase 0 steps one half-track (Sather, p. 9-8) and arms settle.
        harness.ReadSwitch(PhaseOffset(1, on: true));
        Assert.That(harness.QuarterTrack, Is.EqualTo(2));

        var duringSettle = harness.ReadSwitch(Q6L);
        Assert.That(duringSettle & 0x80, Is.EqualTo(0), "A tight poll during track-step settle clears bit 7.");
        Assert.That(duringSettle, Is.EqualTo((byte)(primed & 0x7F)));

        harness.Advance(256);
        var stillSettling = harness.ReadSwitch(Q6L);
        Assert.That(stillSettling & 0x80, Is.EqualTo(0), "Bit 7 stays clear until the settle window expires.");

        harness.Advance(DiskIIController.DefaultTrackStepSettleCycles);
        var fresh = harness.ReadSwitch(Q6L);
        Assert.That(fresh, Is.EqualTo(0xFF), "After settle, the half-track under the head is a fresh gap byte.");
        Assert.That(fresh & 0x80, Is.EqualTo(0x80));
    }

    /// <summary>
    /// A ProDOS DIIDD phase on/off sequence seeks from track 6 back to track 0.
    /// Each half-track is phase-on, then the previous phase off (Sather, p. 9-8).
    /// Descending phases move toward track 0. Two phases make one track.
    /// </summary>
    [Test]
    public void ProdosPhaseOnOff_SeeksFromTrack6ToTrack0()
    {
        var harness = new TimingHarness(new InMemoryTrackMedia(new byte[GcrEncoder.StandardTrackLength]), motorSettleCycles: 0);
        harness.ReadSwitch(MotorOn);

        for (var step = 0; step < 12; step++)
        {
            StepHalfTrack(harness, towardTrackZero: false);
        }

        Assert.That(harness.QuarterTrack, Is.EqualTo(24), "Twelve inward half-tracks land on track 6.");

        for (var step = 0; step < 12; step++)
        {
            var before = harness.QuarterTrack;
            StepHalfTrack(harness, towardTrackZero: true);
            Assert.That(harness.QuarterTrack, Is.EqualTo(before - 2));
        }

        // DIIDD leaves every phase off after the seek (Sather, pp. 9-8 and 9-43 to 9-44).
        var phaseStillOn = (harness.QuarterTrack >> 1) & 3;
        harness.ReadSwitch(PhaseOffset(phaseStillOn, on: false));

        Assert.Multiple(() =>
        {
            Assert.That(harness.QuarterTrack, Is.EqualTo(0));
            Assert.That(harness.PhaseLatch, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Continuous <c>$C0EC</c> polling of a GCR track built in the test decodes every
    /// address field: prologue, volume, track, sector, checksum, and epilogue.
    /// </summary>
    [Test]
    public void ContinuousPolling_DecodesGcrAddressFields()
    {
        var encoded = EncodeTrack(Volume, track: 0);
        var harness = new TimingHarness(new InMemoryTrackMedia(encoded), motorSettleCycles: 0);
        harness.ReadSwitch(MotorOn);

        var fresh = new List<byte>(encoded.Length);
        var sawClearedBit7 = false;
        var polls = 0;
        var pollLimit = (encoded.Length * 8) + 8;
        while (fresh.Count < encoded.Length)
        {
            polls++;
            Assert.That(polls, Is.LessThanOrEqualTo(pollLimit), "Polling did not deliver a full revolution of fresh nibbles.");
            harness.Advance(PollCycles);
            var sample = harness.ReadSwitch(Q6L);
            if ((sample & 0x80) == 0)
            {
                sawClearedBit7 = true;
                continue;
            }

            fresh.Add(sample);
        }

        Assert.That(sawClearedBit7, Is.True, "The BPL poll between bytes clears bit 7.");
        Assert.That(fresh, Is.EqualTo(encoded));

        const int fieldStart = 48;
        Assert.Multiple(() =>
        {
            Assert.That(fresh[fieldStart], Is.EqualTo(GcrEncoder.AddressPrologue1));
            Assert.That(fresh[fieldStart + 1], Is.EqualTo(GcrEncoder.AddressPrologue2));
            Assert.That(fresh[fieldStart + 2], Is.EqualTo(GcrEncoder.AddressPrologue3));
        });

        var fields = DecodeAddressFields(fresh);
        Assert.That(fields, Has.Count.EqualTo(16));
        for (var sector = 0; sector < fields.Count; sector++)
        {
            var field = fields[sector];
            Assert.Multiple(() =>
            {
                Assert.That(field.Volume, Is.EqualTo(Volume));
                Assert.That(field.Track, Is.EqualTo(0));
                Assert.That(field.Sector, Is.EqualTo(sector));
                Assert.That(field.Checksum, Is.EqualTo((byte)(field.Volume ^ field.Track ^ field.Sector)));
                Assert.That(field.EpilogueMatches, Is.True, "Epilogue is DE AA EB.");
            });
        }
    }

    private static byte[] EncodeTrack(byte volume, byte track)
    {
        var sectorData = new byte[GcrEncoder.BytesPerSector * 16];
        var encoded = new byte[GcrEncoder.StandardTrackLength];
        GcrEncoder.EncodeTrack(volume, track, sectorData, encoded);
        return encoded;
    }

    private static byte[] WriteTableTrack()
    {
        var table = GcrEncoder.GetWriteTable();
        var track = new byte[GcrEncoder.StandardTrackLength];
        for (var i = 0; i < track.Length; i++)
        {
            track[i] = table[i % table.Length];
        }

        return track;
    }

    private static byte PhaseOffset(int phase, bool on) => (byte)(0xE0 + (phase * 2) + (on ? 1 : 0));

    private static void StepHalfTrack(TimingHarness harness, bool towardTrackZero)
    {
        var current = (harness.QuarterTrack >> 1) & 3;
        var next = (current + (towardTrackZero ? 3 : 1)) & 3;
        harness.ReadSwitch(PhaseOffset(next, on: true));
        harness.ReadSwitch(PhaseOffset(current, on: false));
    }

    private static List<ObservedAddressField> DecodeAddressFields(IReadOnlyList<byte> nibbles)
    {
        var found = new List<ObservedAddressField>();
        for (var i = 0; i + 13 < nibbles.Count; i++)
        {
            if (nibbles[i] != GcrEncoder.AddressPrologue1
                || nibbles[i + 1] != GcrEncoder.AddressPrologue2
                || nibbles[i + 2] != GcrEncoder.AddressPrologue3)
            {
                continue;
            }

            var volume = Decode44(nibbles[i + 3], nibbles[i + 4]);
            var track = Decode44(nibbles[i + 5], nibbles[i + 6]);
            var sector = Decode44(nibbles[i + 7], nibbles[i + 8]);
            var checksum = Decode44(nibbles[i + 9], nibbles[i + 10]);
            var epilogue = nibbles[i + 11] == GcrEncoder.EpiloguePrologue1
                && nibbles[i + 12] == GcrEncoder.EpiloguePrologue2
                && nibbles[i + 13] == GcrEncoder.EpiloguePrologue3;
            found.Add(new ObservedAddressField(volume, track, sector, checksum, epilogue));
            i += 13;
        }

        return found;
    }

    private static byte Decode44(byte high, byte low) => (byte)(((high << 1) | 1) & low);

    private readonly record struct ObservedAddressField(
        byte Volume,
        byte Track,
        byte Sector,
        byte Checksum,
        bool EpilogueMatches);

    /// <summary>
    /// Slot-6 soft-switch harness. The controller is constructed with no boot ROM.
    /// </summary>
    private sealed class TimingHarness
    {
        private readonly DiskIIController controller;
        private readonly IOPageDispatcher dispatcher;
        private readonly Scheduler scheduler;
        private readonly BusAccess access;

        public TimingHarness(I525Media media, int motorSettleCycles)
        {
            this.controller = new DiskIIController(
                Generator.Log().Object,
                motorSettleCycles: motorSettleCycles);
            this.controller.SlotNumber = 6;
            this.dispatcher = new IOPageDispatcher();
            this.dispatcher.InstallSlotHandlers(6, this.controller.IOHandlers!);
            this.scheduler = new Scheduler();
            var bus = new Mock<IMemoryBus>();
            var signals = new Mock<ISignalBus>();
            var eventContext = new EventContext(this.scheduler, signals.Object, bus.Object);
            this.scheduler.SetEventContext(eventContext);
            this.controller.Initialize(eventContext);
            this.controller.Mount(0, media);
            this.access = new BusAccess(
                Address: 0xC0E0,
                Value: 0,
                WidthBits: 8,
                Mode: BusAccessMode.Decomposed,
                EmulationFlag: true,
                Intent: AccessIntent.DataRead,
                SourceId: 0,
                Cycle: 0,
                Flags: AccessFlags.None);
        }

        public DiskIIController Controller => this.controller;

        public int QuarterTrack => this.controller.GetDriveSnapshot(0).QuarterTrack;

        public int PhaseLatch => this.controller.GetDriveSnapshot(0).PhaseLatch;

        public void Advance(int cycles) => this.scheduler.Advance(new Cycle((ulong)cycles));

        public byte ReadSwitch(byte offset)
        {
            var busAccess = this.access;
            return this.dispatcher.Read(offset, in busAccess);
        }
    }

    /// <summary>
    /// One in-memory track. Whole-track 0 returns that track; every other quarter-track is gap.
    /// </summary>
    private sealed class InMemoryTrackMedia : I525Media
    {
        private readonly byte[] trackZero;

        public InMemoryTrackMedia(byte[] trackZero)
        {
            this.trackZero = trackZero;
        }

        public DiskGeometry Geometry => DiskGeometry.Standard525Dos;

        public int OptimalTrackLength => this.trackZero.Length;

        public bool IsReadOnly => true;

        public void ReadTrack(int quarterTrack, Span<byte> destination)
        {
            if (destination.Length != this.trackZero.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination));
            }

            if (quarterTrack == 0)
            {
                this.trackZero.CopyTo(destination);
                return;
            }

            destination.Fill(0xFF);
        }

        public void WriteTrack(int quarterTrack, ReadOnlySpan<byte> source)
            => throw new InvalidOperationException(
                $"Read-only media rejected a write of {source.Length} bytes at quarter-track {quarterTrack}.");

        public void Flush()
        {
        }
    }
}