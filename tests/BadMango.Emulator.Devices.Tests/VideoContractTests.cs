// <copyright file="VideoContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// Locks the step-2 contract shapes that are not covered by the lease and pairing tests:
/// exact option defaults, the always-empty write log, and the scanner placeholder.
/// </summary>
[TestFixture]
public class VideoContractTests
{
    /// <summary>
    /// Provider option records expose only the exact v1 defaults.
    /// </summary>
    [Test]
    public void FrameProviderOptions_UseExactDefaults()
    {
        var monochrome = new MonochromeOptions();
        var ntsc = new NtscOptions();
        var rgb = new RgbOptions();

        Assert.Multiple(() =>
        {
            Assert.That(monochrome.ScanlineGaps, Is.False);
            Assert.That(ntsc.ScanlineGaps, Is.False);
            Assert.That(ntsc.Decoder, Is.EqualTo(NtscDecoder.FourDotWindow));
            Assert.That(ntsc.ColorKillerLagLines, Is.EqualTo(0));
            Assert.That(rgb.ScanlineGaps, Is.False);
            Assert.That(rgb.Brightness, Is.EqualTo(1.0f));
        });
    }

    /// <summary>
    /// The switch and memory enums keep the members named in the spec, in that order.
    /// </summary>
    [Test]
    public void Enums_MatchTheContractNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetNames<DisplaySwitch>(), Is.EqualTo(new[]
            {
                nameof(DisplaySwitch.Text),
                nameof(DisplaySwitch.Mixed),
                nameof(DisplaySwitch.Page2),
                nameof(DisplaySwitch.HiRes),
                nameof(DisplaySwitch.Col80),
                nameof(DisplaySwitch.Store80),
                nameof(DisplaySwitch.DoubleHiRes),
                nameof(DisplaySwitch.AltCharSet),
                nameof(DisplaySwitch.NoFlash1),
                nameof(DisplaySwitch.NoFlash2),
                nameof(DisplaySwitch.AltGlyph1),
                nameof(DisplaySwitch.AltGlyph2),
            }));
            Assert.That(Enum.GetNames<ScanlineFlags>(), Is.EqualTo(new[]
            {
                nameof(ScanlineFlags.None),
                nameof(ScanlineFlags.ColorBurst),
            }));
            Assert.That(Enum.GetNames<NtscDecoder>(), Is.EqualTo(new[] { nameof(NtscDecoder.FourDotWindow) }));
            Assert.That(Enum.GetNames<VideoMemoryBank>(), Is.EqualTo(new[]
            {
                nameof(VideoMemoryBank.Main),
                nameof(VideoMemoryBank.Aux),
            }));
        });
    }

    /// <summary>
    /// Switch-log spans are computed from the preallocated array on every read, and the write log stays empty.
    /// </summary>
    [Test]
    public void CapturedFrame_SwitchLogIsComputedEachRead_AndWriteLogIsEmpty()
    {
        var events = new DisplaySwitchEvent[2];
        events[0] = new DisplaySwitchEvent(5, DisplaySwitch.Text, false);
        var frame = new CapturedFrame<CaptureState>(
            sequence: 1,
            cycle: 20,
            frameStartCycle: 3,
            state: new CaptureState(),
            switchEvents: events,
            switchCount: 1,
            logOverflowed: true);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Sequence, Is.EqualTo(1));
            Assert.That(frame.Cycle, Is.EqualTo(20));
            Assert.That(frame.FrameStartCycle, Is.EqualTo(3));
            Assert.That(frame.LogOverflowed, Is.True);
            Assert.That(frame.SwitchLog.Length, Is.EqualTo(1));
            Assert.That(frame.SwitchLog[0].Switch, Is.EqualTo(DisplaySwitch.Text));
            Assert.That(frame.WriteLog.Length, Is.EqualTo(0));
            Assert.That(frame.WriteLogOverflowed, Is.False);
        });

        events[0] = new DisplaySwitchEvent(9, DisplaySwitch.HiRes, true);
        ReadOnlySpan<DisplaySwitchEvent> again = frame.SwitchLog;

        Assert.That(again.Length, Is.EqualTo(1));
        Assert.That(again[0].Switch, Is.EqualTo(DisplaySwitch.HiRes));
        Assert.That(frame.WriteLog.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// The scanner contract accepts a captured frame and the empty scan-frame placeholder.
    /// </summary>
    [Test]
    public void Scanner_AcceptsCapturedFrameAndScanFramePlaceholder()
    {
        var scanner = new StubScanner();
        var frame = new CapturedFrame<CaptureState>(2, 4, 1, new CaptureState(), [], 0, false);
        var destination = new ScanFrame();

        scanner.Scan(frame, destination);

        Assert.That(scanner.Calls, Is.EqualTo(1));
    }

    /// <summary>
    /// <see cref="VideoPipelineStats.LatestSequence"/> is a <see cref="ulong"/> and the other counters are <see cref="long"/>.
    /// </summary>
    [Test]
    public void VideoPipelineStats_LatestSequenceIsUlong_AndOtherCountersAreLong()
    {
        var stats = default(VideoPipelineStats) with { PoolExhausted = 4, LatestSequence = 3 };

        Assert.Multiple(() =>
        {
            Assert.That(stats.PoolExhausted, Is.EqualTo(4));
            Assert.That(stats.LatestSequence, Is.EqualTo(3));
            Assert.That(stats.FramesCaptured, Is.EqualTo(0));
        });
    }

    private sealed class CaptureState
    {
    }

    private sealed class StubScanner : IVideoScanner<CaptureState>
    {
        public int Calls { get; private set; }

        public void Scan(CapturedFrame<CaptureState> frame, ScanFrame destination)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ArgumentNullException.ThrowIfNull(destination);
            Calls++;
        }
    }
}