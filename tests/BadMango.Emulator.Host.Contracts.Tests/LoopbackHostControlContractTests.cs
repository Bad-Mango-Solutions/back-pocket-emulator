// <copyright file="LoopbackHostControlContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Runs the shared control-plane contract against the stub loopback.
/// </summary>
[TestFixture]
public sealed class LoopbackHostControlContractTests : HostControlContractTests
{
    /// <inheritdoc />
    protected override IHostSession Open()
    {
        return HostSessions.Open();
    }

    /// <inheritdoc />
    protected override MachineId Add(IHostSession session, string name, PowerCapabilities? power = null, bool startPaused = false)
    {
        return HostSessions.Add((LoopbackHostSession)session, name, power, startPaused);
    }
}