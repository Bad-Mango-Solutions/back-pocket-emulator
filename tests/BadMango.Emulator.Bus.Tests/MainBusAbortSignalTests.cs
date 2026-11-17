// <copyright file="MainBusAbortSignalTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Signaling;

/// <summary>
/// <see cref="SignalLine.Abort"/> follows the fault-status latch.
/// </summary>
[TestFixture]
public class MainBusAbortSignalTests
{
    /// <summary>
    /// A latched non-debug unmapped access asserts ABORT once. A second fault does not
    /// assert again. Clearing status deasserts, and a listener observes both edges.
    /// </summary>
    [Test]
    public void UnmappedAccess_AssertsAbortOnce_ClearDeasserts()
    {
        var signals = new SignalBus();
        var bus = new MainBus(16, signals: signals);
        var edges = new List<(bool Asserted, int SourceId, ulong Cycle)>();
        signals.SignalChanged += (line, asserted, sourceId, cycle) =>
        {
            if (line == SignalLine.Abort)
            {
                edges.Add((asserted, sourceId, cycle.Value));
            }
        };

        bus.TryRead8(Access(0x2000, sourceId: 4, cycle: 80));
        bus.TryRead8(Access(0x2100, sourceId: 9, cycle: 81));

        Assert.Multiple(() =>
        {
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.True);
            Assert.That(signals.AttentionPending, Is.True);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x2000u));
            Assert.That(edges, Has.Count.EqualTo(1));
            Assert.That(edges[0].Asserted, Is.True);
            Assert.That(edges[0].SourceId, Is.EqualTo(4));
            Assert.That(edges[0].Cycle, Is.EqualTo(80ul));
        });

        bus.ClearFaultStatus();

        Assert.Multiple(() =>
        {
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
            Assert.That(signals.AttentionPending, Is.False);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(edges, Has.Count.EqualTo(2));
            Assert.That(edges[1].Asserted, Is.False);
            Assert.That(edges[1].SourceId, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// The bus calls <see cref="ISignalBus.Assert"/> once, with the faulting access's source id
    /// and cycle, and <see cref="ISignalBus.Deassert"/> with that same source id.
    /// </summary>
    [Test]
    public void RecordingBus_SeesOneAssert_AndMatchingDeassert()
    {
        var signals = new RecordingSignalBus();
        var bus = new MainBus(16, signals: signals);

        bus.TryRead8(Access(0x3000, sourceId: 42, cycle: 99));
        bus.TryRead8(Access(0x3001, sourceId: 7, cycle: 100));
        bus.ClearFaultStatus();

        Assert.Multiple(() =>
        {
            Assert.That(signals.Asserts, Is.EqualTo(new[] { (SignalLine.Abort, 42, 99ul) }));
            Assert.That(signals.Deasserts, Is.EqualTo(new[] { (SignalLine.Abort, 42, 99ul) }));
        });
    }

    /// <summary>
    /// A machine reset deasserts ABORT with the reset cycle.
    /// </summary>
    [Test]
    public void Reset_DeassertsAbort()
    {
        var signals = new RecordingSignalBus();
        var bus = new MainBus(16, signals: signals);
        bus.TryRead8(Access(0x1000, sourceId: 1, cycle: 5));

        bus.OnResetAsserted(20);

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(signals.Asserts, Has.Count.EqualTo(1));
            Assert.That(signals.Deasserts, Is.EqualTo(new[] { (SignalLine.Abort, 1, 20ul) }));
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
        });
    }

    /// <summary>
    /// A debug read does not latch and does not assert.
    /// </summary>
    [Test]
    public void DebugRead_DoesNotAssertAbort()
    {
        var signals = new RecordingSignalBus();
        var bus = new MainBus(16, signals: signals);

        bus.TryRead8(Access(0x2000, sourceId: 3, cycle: 1, debug: true));

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(signals.Asserts, Is.Empty);
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
        });
    }

    /// <summary>
    /// A bus constructed without a signal bus latches status and does not throw.
    /// </summary>
    [Test]
    public void WithoutSignalBus_LatchesStatus_AndDoesNotThrow()
    {
        var bus = new MainBus();

        BusResult<byte> result = default;
        Assert.DoesNotThrow(() => result = bus.TryRead8(Access(0x2000, sourceId: 0, cycle: 1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x2000u));
        });

        Assert.DoesNotThrow(() => bus.ClearFaultStatus());
        Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
    }

    /// <summary>
    /// An asserted ABORT line makes <see cref="SignalBus.AttentionPending"/> true until it drops.
    /// </summary>
    [Test]
    public void Abort_RaisesAttentionUntilDeasserted()
    {
        var signals = new SignalBus();
        Assert.That(signals.AttentionPending, Is.False);

        signals.Assert(SignalLine.Abort, deviceId: 0, cycle: 3);

        Assert.Multiple(() =>
        {
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.True);
            Assert.That(signals.AttentionPending, Is.True);
        });

        signals.Deassert(SignalLine.Abort, deviceId: 0, cycle: 4);

        Assert.Multiple(() =>
        {
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
            Assert.That(signals.AttentionPending, Is.False);
        });
    }

    private static BusAccess Access(Addr address, int sourceId, ulong cycle, bool debug = false) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: debug ? AccessIntent.DebugRead : AccessIntent.DataRead,
            SourceId: sourceId,
            Cycle: cycle,
            Flags: debug ? AccessFlags.NoSideEffects : AccessFlags.None);

    /// <summary>
    /// Counts abort assertions so a second fault can be distinguished from a level that stays high.
    /// </summary>
    private sealed class RecordingSignalBus : ISignalBus
    {
        private readonly HashSet<int> abortHeld = [];

        /// <inheritdoc/>
        public event Action<SignalLine, bool, int, Cycle>? SignalChanged;

        public List<(SignalLine Line, int SourceId, ulong Cycle)> Asserts { get; } = [];

        public List<(SignalLine Line, int SourceId, ulong Cycle)> Deasserts { get; } = [];

        /// <inheritdoc/>
        public void Assert(SignalLine line, int deviceId, Cycle cycle)
        {
            Asserts.Add((line, deviceId, cycle.Value));
            if (line == SignalLine.Abort)
            {
                abortHeld.Add(deviceId);
            }

            SignalChanged?.Invoke(line, true, deviceId, cycle);
        }

        /// <inheritdoc/>
        public void Deassert(SignalLine line, int deviceId, Cycle cycle)
        {
            Deasserts.Add((line, deviceId, cycle.Value));
            if (line == SignalLine.Abort)
            {
                abortHeld.Remove(deviceId);
            }

            SignalChanged?.Invoke(line, false, deviceId, cycle);
        }

        /// <inheritdoc/>
        public bool IsAsserted(SignalLine line) => line == SignalLine.Abort && abortHeld.Count > 0;

        /// <inheritdoc/>
        public bool ConsumeNmiEdge() => false;
    }
}