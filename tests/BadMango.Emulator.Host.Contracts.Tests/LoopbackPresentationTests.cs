// <copyright file="LoopbackPresentationTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Stub composite and RGB presentation, plus a PCM block. The samples are synthetic.
/// </summary>
[TestFixture]
public class LoopbackPresentationTests
{
    /// <summary>
    /// A composite window can choose a monitor, and the published sample is the same object the tap handed in.
    /// </summary>
    [Test]
    public void CompositeSignalAndBgraFrame_AreSharedWithoutACopy()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        ILoopbackMachineEdge edge = session;
        using IPresentationSubscription mono = session.Presentation.Subscribe(id, "left", HostMonitorChoice.Mono);
        using IPresentationSubscription amber = session.Presentation.Subscribe(id, "right", HostMonitorChoice.Amber);

        byte[] pixels = [0x00, 0x00, 0x00, 0xFF, 0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF, 0x70, 0x80, 0x90, 0xFF];
        HostBgraFrame frame = new(1, 17_030, 2, 2, 8, pixels);
        HostCompositeSignal signal = new(1, 17_030, 2, 1, new byte[] { 0xA5 }, new byte[] { 0x01 });
        HostPresentationSample sample = new(HostVideoKind.Composite, 1, 17_030, frame, signal);
        edge.PublishFrame(id, sample);

        Assert.Multiple(() =>
        {
            Assert.That(mono.TryReadLatest(out HostPresentationSample? left), Is.True);
            Assert.That(left, Is.SameAs(sample));
            Assert.That(amber.TryReadLatest(out HostPresentationSample? right), Is.True);
            Assert.That(right, Is.SameAs(sample));
            Assert.That(mono.Monitor, Is.EqualTo(HostMonitorChoice.Mono));
            Assert.That(amber.Monitor, Is.EqualTo(HostMonitorChoice.Amber));
            Assert.That(mono.Timing.LastSequence, Is.EqualTo(1));
            Assert.That(mono.Timing.LastCycle, Is.EqualTo(17_030));
        });

        HostPresentationSample older = new(HostVideoKind.Composite, 9, 1, frame, signal);
        edge.PublishFrame(id, older);
        Assert.That(mono.TryReadLatest(out HostPresentationSample? latest), Is.True);
        Assert.That(latest, Is.SameAs(older));
        Assert.Throws<InvalidOperationException>(() => mono.SetMonitor(HostMonitorChoice.Rgb));
    }

    /// <summary>
    /// RGB refuses a composite monitor and a raw signal.
    /// </summary>
    [Test]
    public void RgbOutput_AcceptsFinishedBgraOnly()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Rgb", output: HostVideoKind.Rgb);
        using IPresentationSubscription window = session.Presentation.Subscribe(id, "main");

        Assert.That(window.Monitor, Is.EqualTo(HostMonitorChoice.Rgb));
        Assert.Throws<InvalidOperationException>(() => window.SetMonitor(HostMonitorChoice.Green));
        Assert.Throws<InvalidOperationException>(() => session.Presentation.Subscribe(id, "other", HostMonitorChoice.Ntsc));

        HostPresentationSample sample = new(HostVideoKind.Rgb, 4, 100, new HostBgraFrame(4, 100, 1, 1, 4, new byte[] { 1, 2, 3, 4 }), null);
        ((ILoopbackMachineEdge)session).PublishFrame(id, sample);
        Assert.That(window.TryReadLatest(out HostPresentationSample? read), Is.True);
        Assert.That(read!.Frame!.Bgra.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        HostCompositeSignal signal = new(4, 100, 1, 1, new byte[] { 1 }, new byte[] { 0 });
        Assert.Throws<InvalidOperationException>(() => ((ILoopbackMachineEdge)session).PublishFrame(id, new HostPresentationSample(HostVideoKind.Rgb, 5, 100, sample.Frame, signal)));
    }

    /// <summary>
    /// PCM blocks carry the line-out format and a cycle stamp, and each window receives them.
    /// </summary>
    [Test]
    public void AudioBlocks_FanOutWithTheLineOutFormat()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        using IPresentationSubscription first = session.Presentation.Subscribe(id, "a");
        using IPresentationSubscription second = session.Presentation.Subscribe(id, "b");
        ILoopbackMachineEdge edge = session;

        Assert.That(first.AudioFormat, Is.EqualTo(HostAudioDefaults.LineOut));

        for (int i = 0; i < 10; i++)
        {
            byte[] pcm = new byte[4];
            pcm[0] = (byte)i;
            edge.PublishAudio(id, new HostAudioBlock((ulong)i, 2, pcm));
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.TryReadAudio(out HostAudioBlock? block), Is.True);
            Assert.That(block!.Cycle, Is.EqualTo(2));
            Assert.That(block.Pcm.Span[0], Is.EqualTo(2));
            Assert.That(second.TryReadAudio(out HostAudioBlock? other), Is.True);
            Assert.That(other!.Cycle, Is.EqualTo(2));
        });

        int count = 1;
        while (first.TryReadAudio(out HostAudioBlock? _))
        {
            count++;
        }

        Assert.That(count, Is.EqualTo(8));
    }

    /// <summary>
    /// Screen text is a snapshot. It does not require a video device.
    /// </summary>
    [Test]
    public void ScreenText_RoundTripsAStub()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        HostScreenText screen = new(40, 24, "READY", false, false, false);
        ((ILoopbackMachineEdge)session).SetScreenText(id, screen);
        ((ILoopbackMachineEdge)session).SetVideoState(id, new HostVideoState(HostVideoKind.Composite, Text: true, Mixed: false, Page2: false, HiRes: false, EightyColumns: false, AltCharSet: true));

        Assert.Multiple(() =>
        {
            Assert.That(session.Presentation.GetScreenText(id).Text, Is.EqualTo("READY"));
            Assert.That(session.Presentation.GetVideoState(id).AltCharSet, Is.True);
        });
    }
}