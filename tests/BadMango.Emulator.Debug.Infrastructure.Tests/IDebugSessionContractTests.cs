// <copyright file="IDebugSessionContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Diagnostics;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Contract tests for <see cref="IDebugSession"/> implementations.
/// </summary>
public abstract class IDebugSessionContractTests
{
    /// <summary>
    /// Harness abstraction used by the contract tests.
    /// </summary>
    protected interface ISessionHarness : IDisposable
    {
        /// <summary>
        /// Gets the session under test.
        /// </summary>
        IDebugSession Session { get; }

        /// <summary>
        /// Gets a value indicating whether the machine is stopped.
        /// </summary>
        bool IsMachineStopped { get; }

        /// <summary>
        /// Loads a tiny test program and resets execution to its start address.
        /// </summary>
        /// <param name="startAddress">The start address.</param>
        /// <param name="bytes">The instruction bytes.</param>
        void LoadProgram(ushort startAddress, params byte[] bytes);

        /// <summary>
        /// Makes the next instruction execution throw.
        /// </summary>
        void ArmExecutionFault();
    }

    /// <summary>
    /// Verifies that stepping executes through the machine lifecycle and advances the PC.
    /// </summary>
    [Test]
    public void Step_AdvancesProgramCounter()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xEA, 0xDB);

        ulong count = harness.Session.Step(2);
        RegisterSnapshot registers = harness.Session.GetRegisters();

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(registers.ProgramCounter, Is.EqualTo(0x1002ul));
        });
    }

    /// <summary>
    /// Verifies deterministic run-until behavior for a program-counter condition.
    /// </summary>
    [Test]
    public void RunUntil_ProgramCounterCondition_ReportsDeterministicCycles()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xEA, 0xEA, 0xDB);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.ProgramCounter, Address: 0x1003),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.Breakpoint));
            Assert.That(result.InstructionCount, Is.EqualTo(3));
            Assert.That(result.CycleCount, Is.EqualTo(6));
            Assert.That(result.ProgramCounter, Is.EqualTo(0x1003u));
        });
    }

    /// <summary>
    /// Verifies that max-cycle budget exhaustion reports <see cref="StopReason.Budget"/>.
    /// </summary>
    [Test]
    public void RunUntil_WhenMaxCyclesExhausted_ReturnsBudget()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0x4C, 0x00, 0x10);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.ProgramCounter, Address: 0x2000),
            new(MaxCycles: 2));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.Budget));
            Assert.That(result.InstructionCount, Is.EqualTo(1));
            Assert.That(result.CycleCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Verifies that a cycle-count condition is distinct from exhausting the run budget.
    /// </summary>
    [Test]
    public void RunUntil_CycleCountCondition_ReportsCycleCount()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xEA, 0xEA, 0xDB);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.CycleCount, CycleLimit: 4),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.CycleCount));
            Assert.That(result.StopReason, Is.Not.EqualTo(StopReason.Budget));
            Assert.That(result.CycleCount, Is.GreaterThanOrEqualTo(4));
        });
    }

    /// <summary>
    /// Verifies that a breakpoint condition stops when the breakpoint is reached.
    /// </summary>
    [Test]
    public void RunUntil_BreakpointCondition_StopsAtBreakpoint()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xEA, 0xEA, 0xDB);
        harness.Session.AddBreakpoint(0x1002);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.Breakpoint, BreakpointAddress: 0x1002),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.Breakpoint));
            Assert.That(result.ProgramCounter, Is.EqualTo(0x1002u));
        });
    }

    /// <summary>
    /// Verifies that a watchpoint condition stops when a store hits the watched address.
    /// </summary>
    [Test]
    public void RunUntil_WatchpointCondition_StopsOnStore()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xA9, 0x42, 0x85, 0x80, 0xDB);
        harness.Session.AddWatchpoint(0x0080, WatchAccess.Write);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.Watchpoint, WatchAddress: 0x0080),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.Watchpoint));
            Assert.That(harness.Session.ReadMemory(0x0080, 1).Data[0], Is.EqualTo(0x42));
        });
    }

    /// <summary>
    /// Verifies that a memory-equals condition stops once the bytes match.
    /// </summary>
    [Test]
    public void RunUntil_MemoryEqualsCondition_StopsWhenBytesMatch()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xA9, 0x42, 0x85, 0x80, 0xDB);

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.MemoryEquals, MemoryAddress: 0x0080, MemoryValue: [0x42]),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.MemoryCondition));
            Assert.That(result.ProgramCounter, Is.EqualTo(0x1004u));
        });
    }

    /// <summary>
    /// Verifies that an execution exception stops the run as a fault and stops the machine.
    /// </summary>
    [Test]
    public void RunUntil_WhenStepThrows_ReturnsFaultAndStopsMachine()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xEA);
        harness.ArmExecutionFault();

        RunResult result = harness.Session.RunUntil(
            new(RunConditionKind.ProgramCounter, Address: 0x2000),
            new(MaxCycles: 20));

        Assert.Multiple(() =>
        {
            Assert.That(result.StopReason, Is.EqualTo(StopReason.Fault));
            Assert.That(harness.IsMachineStopped, Is.True);
        });
    }

    /// <summary>
    /// Verifies background run start/poll/stop behavior.
    /// </summary>
    [Test]
    public void BackgroundRun_StartPollStop_CancelsRun()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0x4C, 0x00, 0x10);

        Guid runId = harness.Session.StartRun(
            new(RunConditionKind.ProgramCounter, Address: 0x2000),
            new(MaxCycles: 1_000_000));

        Assert.That(harness.Session.GetRunStatus(runId), Is.Null);
        Assert.That(harness.Session.StopRun(runId), Is.True);

        RunResult? result = WaitForRunResult(harness.Session, runId, TimeSpan.FromSeconds(2));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StopReason, Is.EqualTo(StopReason.Cancelled));
    }

    /// <summary>
    /// Creates a test harness that provides a configured debug session.
    /// </summary>
    /// <returns>The configured harness.</returns>
    protected abstract ISessionHarness CreateHarness();

    private static RunResult? WaitForRunResult(IDebugSession session, Guid runId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            RunResult? result = session.GetRunStatus(runId);
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(10);
        }

        return session.GetRunStatus(runId);
    }
}