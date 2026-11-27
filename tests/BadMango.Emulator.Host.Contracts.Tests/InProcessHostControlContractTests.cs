// <copyright file="InProcessHostControlContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using BadMango.Emulator.Host;

/// <summary>
/// Runs the shared control-plane contract against the real in-process host.
/// </summary>
[TestFixture]
[CancelAfter(20000)]
public sealed class InProcessHostControlContractTests : HostControlContractTests
{
    /// <inheritdoc />
    protected override IHostSession Open()
    {
        return new InProcessHostTransport().Connect(HostApi.Version);
    }

    /// <inheritdoc />
    protected override MachineId Add(IHostSession session, string name, PowerCapabilities? power = null, bool startPaused = false)
    {
        BootProfile.Ensure(session.Control);
        return session.Control.CreateInstance(new HostInstanceSpec(
            name,
            BootProfile.Id,
            startPaused,
            power,
            CpuSummary: "65C02"));
    }
}