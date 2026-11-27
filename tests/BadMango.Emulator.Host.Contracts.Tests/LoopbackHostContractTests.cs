// <copyright file="LoopbackHostContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Version, addressing, and reference-boundary tests for the host contracts.
/// </summary>
[TestFixture]
public class LoopbackHostContractTests
{
    /// <summary>
    /// Two machines on one host do not share power, disks, or the latest frame.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task TwoMachines_StayIndependent()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId first = HostSessions.Add(session, "One", new PowerCapabilities(true, true, true));
        MachineId second = HostSessions.Add(session, "Two");
        await session.Control.PowerOnAsync(first);
        await session.Control.MountDiskAsync(first, new DriveId(6, 1), "one.dsk", false);
        session.Input.KeyDown(second, HostKey.Escape, HostKeyModifiers.None);

        HostBgraFrame frame = new(3, 10, 1, 1, 4, new byte[] { 9, 8, 7, 6 });
        ((ILoopbackMachineEdge)session).PublishFrame(first, new HostPresentationSample(HostVideoKind.Composite, 3, 10, frame, new HostCompositeSignal(3, 10, 1, 1, new byte[] { 1 }, new byte[] { 0 })));
        using IPresentationSubscription other = session.Presentation.Subscribe(second, "window");

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.ListInstances(), Has.Count.EqualTo(2));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(session.Control.GetMachineInfo(first).State, Is.EqualTo(HostRunState.Running));
            Assert.That(session.Control.GetMachineInfo(second).State, Is.EqualTo(HostRunState.Off));
            Assert.That(session.Control.ListDisks(second)[0].Drives[0].ImagePath, Is.Null);
            Assert.That(session.Control.ListDisks(first)[0].Drives[0].ImageName, Is.EqualTo("one.dsk"));
            Assert.That(session.Input.GetInputState(first).KeysDown, Is.Empty);
            Assert.That(session.Input.GetInputState(second).KeysDown, Is.EqualTo(new[] { HostKey.Escape }));
            Assert.That(other.TryReadLatest(out _), Is.False);
            Assert.That(session.Transport.Kind, Is.EqualTo(HostTransportKind.Loopback));
            Assert.That(session.Transport.StatusLine, Is.EqualTo("Loopback · healthy"));
            Assert.That(session.ApiVersion, Is.EqualTo(HostApi.Version));
        });
    }

    /// <summary>
    /// A client that asks for another version is refused before a session exists.
    /// </summary>
    [Test]
    public void Connect_RejectsADifferentVersion()
    {
        LoopbackHostTransport transport = new();
        IHostTransport asInterface = transport;

        HostApiVersionException thrown = Assert.Throws<HostApiVersionException>(() => asInterface.Connect(HostApi.Version + 1))!;

        Assert.Multiple(() =>
        {
            Assert.That(thrown.Requested, Is.EqualTo(2));
            Assert.That(thrown.Supported, Is.EqualTo(HostApi.Version));
            Assert.That(transport.Name, Is.EqualTo("loopback"));
        });
    }

    /// <summary>
    /// The contract assembly does not reference the machine, the video device, or the audio output.
    /// </summary>
    [Test]
    public void ContractAssembly_DoesNotReferenceMachineVideoOrAudio()
    {
        string[] names = typeof(IHostSession).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Core"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Devices"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Rendering"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Systems"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Storage"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Debug.Abstractions"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Debug.Infrastructure"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.UI"));
        });
    }
}