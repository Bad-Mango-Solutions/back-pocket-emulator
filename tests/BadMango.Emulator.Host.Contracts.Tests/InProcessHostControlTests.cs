// <copyright file="InProcessHostControlTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using BadMango.Emulator.Host;

/// <summary>
/// Transport and unbound-plane checks for the real in-process host.
/// </summary>
[TestFixture]
public class InProcessHostControlTests
{
    /// <summary>
    /// A client that asks for another version is refused.
    /// </summary>
    [Test]
    public void Connect_RejectsADifferentVersion()
    {
        InProcessHostTransport transport = new();

        HostApiVersionException thrown = Assert.Throws<HostApiVersionException>(() => transport.Connect(HostApi.Version + 1))!;

        Assert.Multiple(() =>
        {
            Assert.That(thrown.Requested, Is.EqualTo(HostApi.Version + 1));
            Assert.That(thrown.Supported, Is.EqualTo(HostApi.Version));
            Assert.That(transport.Name, Is.EqualTo("loopback"));
        });
    }

    /// <summary>
    /// Unsupported planes keep the session shape stable with slice-scoped exceptions.
    /// </summary>
    [Test]
    public void UnsupportedPlanes_ThrowSliceNotSupported()
    {
        using InProcessHostSession session = new InProcessHostTransport().Connect(HostApi.Version);

        NotSupportedException presentation = Assert.Throws<NotSupportedException>(
            () => session.Presentation.GetScreenText(new MachineId("machine")))!;
        NotSupportedException input = Assert.Throws<NotSupportedException>(
            () => session.Input.GetInputState(new MachineId("machine")))!;
        NotSupportedException debugger = Assert.Throws<NotSupportedException>(
            () => session.Debugger.IsAttached(new MachineId("machine")))!;

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Message, Does.Contain("#810"));
            Assert.That(input.Message, Does.Contain("#809"));
            Assert.That(debugger.Message, Does.Contain("#813"));
        });
    }
}