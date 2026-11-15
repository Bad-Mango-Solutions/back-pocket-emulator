// <copyright file="MachineDebugSessionTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Diagnostics;
using System.Reflection;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Abstractions;

using Moq;

/// <summary>
/// Contract and focused behavior tests for <see cref="MachineDebugSession"/>.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionTests : IDebugSessionContractTests
{
    /// <summary>
    /// Verifies run faults are surfaced as <see cref="StopReason.Fault"/> and leave the machine stopped.
    /// </summary>
    [Test]
    public void RunUntil_WhenStepThrows_ReturnsFaultAndStopsMachine()
    {
        var cpu = new Mock<ICpu>(MockBehavior.Strict);
        cpu.SetupGet(x => x.Halted).Returns(false);
        cpu.Setup(x => x.GetPC()).Returns(0x1000u);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
        machine.Setup(x => x.Step()).Throws(new InvalidOperationException("simulated run fault"));
        machine.Setup(x => x.Stop());

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Machine).Returns(machine.Object);
        context.SetupGet(x => x.Breakpoints).Returns(new BreakpointManager());
        context.SetupGet(x => x.Watchpoints).Returns(new WatchpointManager());

        var session = new MachineDebugSession(context.Object);
        RunResult result = session.RunUntil(
            new(RunConditionKind.ProgramCounter, Address: 0x2000),
            new(MaxCycles: 100));

        Assert.That(result.StopReason, Is.EqualTo(StopReason.Fault));
        machine.Verify(x => x.Stop(), Times.Once);
    }

    /// <summary>
    /// Verifies debugger reads and writes of a soft switch do not toggle it.
    /// </summary>
    [Test]
    public void WriteMemory_ToSoftSwitch_DoesNotToggleTheSwitch()
    {
        var dispatcher = new IOPageDispatcher();
        var ioPage = new CompositeIOTarget("I/O Page", dispatcher, new SlotManager(dispatcher));
        var controller = new AuxiliaryMemoryController();
        controller.RegisterHandlers(dispatcher);

        var bus = new MainBus();
        bus.MapPage(0xC000 >> 12, new(1, RegionTag.Io, PagePerms.ReadWrite, TargetCaps.None, ioPage, 0));

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.Bus).Returns(bus);
        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Machine).Returns(machine.Object);
        var session = new MachineDebugSession(context.Object);

        var cpuWrite = new BusAccess(
            Address: 0xC055,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataWrite,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
        var cpuClear = cpuWrite with { Address = 0xC054 };

        Assert.That(bus.TryWrite8(cpuWrite, 0).Ok, Is.True, "PAGE2 write did not reach the soft-switch handler.");
        Assert.That(controller.IsPage2Selected, Is.True, "A CPU write to $C055 should select PAGE2.");
        Assert.That(bus.TryWrite8(cpuClear, 0).Ok, Is.True);
        Assert.That(controller.IsPage2Selected, Is.False);

        session.ReadMemory(0xC055, 1);
        Assert.That(controller.IsPage2Selected, Is.False, "Session ReadMemory toggled PAGE2.");

        session.WriteMemory(0xC055, [0x00]);
        Assert.That(controller.IsPage2Selected, Is.False, "Session WriteMemory toggled PAGE2.");
    }

    /// <summary>
    /// Verifies an invalid background-run condition fails before the run is scheduled.
    /// </summary>
    [Test]
    public void StartRun_InvalidCondition_ThrowsArgumentExceptionImmediately()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xDB);

        var condition = new RunCondition(RunConditionKind.ProgramCounter);
        Assert.Throws<ArgumentException>(() => harness.Session.StartRun(condition, new RunBudget(MaxCycles: 20)));
    }

    /// <summary>
    /// Verifies that stopping a run after it has finished does not touch a disposed cancellation source.
    /// </summary>
    [Test]
    public void StopRun_AfterCompletion_DoesNotThrow()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xDB);

        for (int i = 0; i < 8; i++)
        {
            Guid runId = harness.Session.StartRun(
                new RunCondition(RunConditionKind.ProgramCounter, Address: 0x1000),
                new RunBudget(MaxCycles: 20));
            RunResult? result = WaitForBackgroundResult(harness.Session, runId);
            Assert.That(result, Is.Not.Null, $"Run {i} did not complete.");

            Exception? error = null;
            bool stopped = false;
            try
            {
                stopped = harness.Session.StopRun(runId);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(error, Is.Null, $"StopRun threw after run {i} finished.");
                Assert.That(stopped, Is.True);
            });
        }
    }

    /// <summary>
    /// Verifies a memory write during a background run is rejected instead of racing the run.
    /// </summary>
    [Test]
    public void WriteMemory_DuringBackgroundRun_IsRejectedWithoutTouchingMemory()
    {
        using var run = BlockedBackgroundRun.Start();
        var write = Task.Run(() => run.Session.WriteMemory(0x0300, [0x5A]));
        (bool completed, Exception? error) = WaitForTask(write, TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True, "WriteMemory blocked while a background run owned execution.");
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            if (error is not null)
            {
                Assert.That(error.Message, Does.Contain("background run").IgnoreCase);
            }

            Assert.That(run.BusWrites, Is.Zero, "WriteMemory mutated the bus while the background run was inside Step.");
        });
    }

    /// <summary>
    /// Verifies a reset during a background run is rejected instead of racing the run.
    /// </summary>
    [Test]
    public void Reset_DuringBackgroundRun_IsRejectedWithoutResetting()
    {
        using var run = BlockedBackgroundRun.Start();
        var reset = Task.Run(() => run.Session.Reset(ResetKind.Hard));
        (bool completed, Exception? error) = WaitForTask(reset, TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True, "Reset blocked while a background run owned execution.");
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            if (error is not null)
            {
                Assert.That(error.Message, Does.Contain("background run").IgnoreCase);
            }

            Assert.That(run.Resets, Is.Zero, "Reset ran while the background run was inside Step.");
        });
    }

    /// <summary>
    /// Verifies a memory read does not wait for a background run that owns execution.
    /// </summary>
    [Test]
    public void ReadMemory_DuringBackgroundRun_DoesNotWaitForExecution()
    {
        using var run = BlockedBackgroundRun.Start();
        var read = Task.Run(() => run.Session.ReadMemory(0x1000, 1));
        (bool completed, Exception? error) = WaitForTask(read, TimeSpan.FromMilliseconds(500));

        Assert.That(completed, Is.True, "ReadMemory blocked while a background run owned execution.");
        Assert.That(error, Is.Null);
        Assert.That(read.Result.Data[0], Is.EqualTo(0xEA));
    }

    /// <summary>
    /// Verifies a write is accepted once the background run has finished.
    /// </summary>
    [Test]
    public void WriteMemory_AfterBackgroundRunStops_WritesTheBus()
    {
        using var run = BlockedBackgroundRun.Start();
        Assert.That(run.Session.StopRun(run.RunId), Is.True);
        run.ReleaseStep();

        RunResult? result = WaitForBackgroundResult(run.Session, run.RunId);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StopReason, Is.EqualTo(StopReason.Cancelled));

        run.Session.WriteMemory(0x0300, [0x5A]);
        Assert.That(run.BusWrites, Is.EqualTo(1));
    }

    /// <summary>
    /// Verifies a write waits for a foreground step instead of entering the bus beside it.
    /// </summary>
    [Test]
    public void WriteMemory_WhileForegroundStepOwnsExecution_DoesNotOverlapTheStep()
    {
        var stepEntered = new ManualResetEventSlim(false);
        var releaseStep = new ManualResetEventSlim(false);
        var writeEntered = new ManualResetEventSlim(false);
        try
        {
            var cpu = new Mock<ICpu>(MockBehavior.Strict);
            cpu.SetupGet(x => x.Halted).Returns(false);

            var bus = new Mock<IMemoryBus>(MockBehavior.Strict);
            bus.Setup(x => x.TryWrite8(It.Ref<BusAccess>.IsAny, It.IsAny<byte>()))
                .Returns(() =>
                {
                    writeEntered.Set();
                    return BusResult.Success();
                });

            var machine = new Mock<IMachine>(MockBehavior.Strict);
            machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
            machine.SetupGet(x => x.Bus).Returns(bus.Object);
            machine.Setup(x => x.Step()).Returns(() =>
            {
                stepEntered.Set();
                Assert.That(releaseStep.Wait(TimeSpan.FromSeconds(3)), Is.True, "Step was not released.");
                return new CpuStepResult(CpuRunState.Running, new Cycle(2));
            });

            var context = new Mock<IDebugContext>(MockBehavior.Strict);
            context.SetupGet(x => x.Machine).Returns(machine.Object);
            var session = new MachineDebugSession(context.Object);

            var stepTask = Task.Run(() => session.Step(1));
            Assert.That(stepEntered.Wait(TimeSpan.FromSeconds(2)), Is.True, "Foreground step did not start.");

            var writeStarted = new ManualResetEventSlim(false);
            var writeTask = Task.Run(() =>
            {
                writeStarted.Set();
                session.WriteMemory(0x0300, [0xAB]);
            });

            Assert.That(writeStarted.Wait(TimeSpan.FromSeconds(2)), Is.True, "Write task did not start.");
            bool overlapped = writeEntered.Wait(TimeSpan.FromMilliseconds(400));
            releaseStep.Set();

            (bool stepCompleted, Exception? stepError) = WaitForTask(stepTask, TimeSpan.FromSeconds(2));
            (bool writeCompleted, Exception? writeError) = WaitForTask(writeTask, TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(overlapped, Is.False, "WriteMemory entered the bus while Step held execution.");
                Assert.That(stepCompleted, Is.True);
                Assert.That(stepError, Is.Null);
                Assert.That(writeCompleted, Is.True);
                Assert.That(writeError, Is.Null);
                Assert.That(writeEntered.IsSet, Is.True, "WriteMemory did not run after Step released execution.");
            });
        }
        finally
        {
            releaseStep.Set();
            stepEntered.Dispose();
            releaseStep.Dispose();
            writeEntered.Dispose();
        }
    }

    /// <summary>
    /// Verifies overlapping memory writes are serialized with each other.
    /// </summary>
    [Test]
    public void WriteMemory_ConcurrentCalls_DoNotOverlap()
    {
        var release = new ManualResetEventSlim(false);
        int inside = 0;
        try
        {
            var bus = new Mock<IMemoryBus>(MockBehavior.Strict);
            bus.Setup(x => x.TryWrite8(It.Ref<BusAccess>.IsAny, It.IsAny<byte>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref inside);
                    Assert.That(release.Wait(TimeSpan.FromSeconds(3)), Is.True, "Write was not released.");
                    Interlocked.Decrement(ref inside);
                    return BusResult.Success();
                });

            var machine = new Mock<IMachine>(MockBehavior.Strict);
            machine.SetupGet(x => x.Bus).Returns(bus.Object);
            var context = new Mock<IDebugContext>(MockBehavior.Strict);
            context.SetupGet(x => x.Machine).Returns(machine.Object);
            var session = new MachineDebugSession(context.Object);

            var firstStarted = new ManualResetEventSlim(false);
            var first = Task.Run(() =>
            {
                firstStarted.Set();
                session.WriteMemory(0x0200, [0x01]);
            });
            Assert.That(firstStarted.Wait(TimeSpan.FromSeconds(2)), Is.True, "First write did not start.");
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref inside) > 0, TimeSpan.FromSeconds(2)), Is.True);

            var secondStarted = new ManualResetEventSlim(false);
            var second = Task.Run(() =>
            {
                secondStarted.Set();
                session.WriteMemory(0x0201, [0x02]);
            });
            Assert.That(secondStarted.Wait(TimeSpan.FromSeconds(2)), Is.True, "Second write did not start.");
            bool overlapped = SpinWait.SpinUntil(() => Volatile.Read(ref inside) > 1, TimeSpan.FromMilliseconds(400));
            release.Set();

            (bool firstCompleted, Exception? firstError) = WaitForTask(first, TimeSpan.FromSeconds(2));
            (bool secondCompleted, Exception? secondError) = WaitForTask(second, TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(overlapped, Is.False, "Concurrent WriteMemory calls overlapped inside the bus write.");
                Assert.That(firstCompleted, Is.True);
                Assert.That(firstError, Is.Null);
                Assert.That(secondCompleted, Is.True);
                Assert.That(secondError, Is.Null);
            });
        }
        finally
        {
            release.Set();
            release.Dispose();
        }
    }

    /// <summary>
    /// Verifies finished runs are removed from the in-flight table and earlier results do not accumulate.
    /// </summary>
    [Test]
    public void CompletedBackgroundRuns_DoNotAccumulate()
    {
        using var harness = CreateHarness();
        harness.LoadProgram(0x1000, 0xEA, 0xDB);
        var session = (MachineDebugSession)harness.Session;
        var runIds = new List<Guid>();

        for (int i = 0; i < 4; i++)
        {
            Guid runId = session.StartRun(
                new RunCondition(RunConditionKind.ProgramCounter, Address: 0x1000),
                new RunBudget(MaxCycles: 20));
            runIds.Add(runId);

            RunResult? result = WaitForBackgroundResult(session, runId);
            Assert.That(result, Is.Not.Null, $"Run {i} did not complete.");
            Assert.That(CountInFlightRuns(session), Is.Zero, $"Run {i} stayed in the in-flight table after completion.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(session.GetRunStatus(runIds[^1]), Is.Not.Null);
            for (int i = 0; i < runIds.Count - 1; i++)
            {
                Assert.That(session.GetRunStatus(runIds[i]), Is.Null, $"Finished run {i} was still retained.");
            }

            Assert.That(CountRetainedRunEntries(session), Is.LessThanOrEqualTo(1));
        });
    }

    /// <inheritdoc />
    protected override ISessionHarness CreateHarness() => new MachineSessionHarness();

    private static RunResult? WaitForBackgroundResult(IDebugSession session, Guid runId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
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

    private static (bool Completed, Exception? Error) WaitForTask(Task task, TimeSpan timeout)
    {
        try
        {
            return (task.Wait(timeout), null);
        }
        catch (AggregateException ex)
        {
            return (true, ex.InnerExceptions.Count == 1 ? ex.InnerException : ex);
        }
    }

    private static int CountInFlightRuns(MachineDebugSession session)
    {
        object table = GetRequiredField(session, "backgroundRuns");
        return ReadCount(table);
    }

    private static int CountRetainedRunEntries(MachineDebugSession session)
    {
        int total = 0;
        foreach (FieldInfo field in typeof(MachineDebugSession).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            object? value = field.GetValue(session);
            if (value is null || !value.GetType().Name.Contains("Dictionary", StringComparison.Ordinal))
            {
                continue;
            }

            total += ReadCount(value);
        }

        return total;
    }

    private static object GetRequiredField(MachineDebugSession session, string fieldName)
    {
        FieldInfo? field = typeof(MachineDebugSession).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}'.");
        object? value = field!.GetValue(session);
        Assert.That(value, Is.Not.Null);
        return value!;
    }

    private static int ReadCount(object value)
    {
        PropertyInfo? count = value.GetType().GetProperty("Count");
        Assert.That(count, Is.Not.Null);
        return (int)count!.GetValue(value)!;
    }

    /// <summary>
    /// A background run parked inside <see cref="IMachine.Step"/> so other calls can race it.
    /// </summary>
    private sealed class BlockedBackgroundRun : IDisposable
    {
        private readonly ManualResetEventSlim releaseStep = new(false);
        private readonly ManualResetEventSlim stepEntered = new(false);
        private int busWrites;
        private int resets;
        private bool disposed;

        private BlockedBackgroundRun()
        {
            var cpu = new Mock<ICpu>(MockBehavior.Strict);
            cpu.SetupGet(x => x.Halted).Returns(false);
            cpu.Setup(x => x.GetPC()).Returns(0x1000u);

            var bus = new Mock<IMemoryBus>(MockBehavior.Strict);
            bus.Setup(x => x.TryWrite8(It.Ref<BusAccess>.IsAny, It.IsAny<byte>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref busWrites);
                    return BusResult.Success();
                });
            bus.Setup(x => x.TryRead8(It.Ref<BusAccess>.IsAny))
                .Returns(BusResult<byte>.Success(0xEA));

            var machine = new Mock<IMachine>(MockBehavior.Strict);
            machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
            machine.SetupGet(x => x.Bus).Returns(bus.Object);
            machine.Setup(x => x.Pause());
            machine.Setup(x => x.Reset(It.IsAny<bool>())).Callback(() => Interlocked.Increment(ref resets));
            machine.Setup(x => x.Step()).Returns(() =>
            {
                stepEntered.Set();
                Assert.That(releaseStep.Wait(TimeSpan.FromSeconds(5)), Is.True, "Blocked step was not released.");
                return new CpuStepResult(CpuRunState.Running, new Cycle(2));
            });

            var context = new Mock<IDebugContext>(MockBehavior.Strict);
            context.SetupGet(x => x.Machine).Returns(machine.Object);
            context.SetupGet(x => x.Breakpoints).Returns(new BreakpointManager());
            context.SetupGet(x => x.Watchpoints).Returns(new WatchpointManager());
            Session = new MachineDebugSession(context.Object);
        }

        /// <summary>
        /// Gets the session whose background run is parked inside a step.
        /// </summary>
        public MachineDebugSession Session { get; }

        /// <summary>
        /// Gets how many bus writes the parked run has observed.
        /// </summary>
        public int BusWrites => Volatile.Read(ref busWrites);

        /// <summary>
        /// Gets how many machine resets the parked run has observed.
        /// </summary>
        public int Resets => Volatile.Read(ref resets);

        /// <summary>
        /// Gets the identifier of the parked background run.
        /// </summary>
        public Guid RunId { get; private set; }

        public static BlockedBackgroundRun Start()
        {
            var run = new BlockedBackgroundRun();
            try
            {
                run.RunId = run.Session.StartRun(
                    new RunCondition(RunConditionKind.ProgramCounter, Address: 0x2000),
                    new RunBudget(MaxCycles: 1_000_000));
                if (!run.stepEntered.Wait(TimeSpan.FromSeconds(2)))
                {
                    Assert.Fail("Background run did not enter Step.");
                }

                return run;
            }
            catch
            {
                run.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Releases the parked step so the background run can observe cancellation.
        /// </summary>
        public void ReleaseStep() => releaseStep.Set();

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                if (RunId != Guid.Empty)
                {
                    Session.StopRun(RunId);
                }
            }
            catch (ObjectDisposedException)
            {
                // The assertion under test records this failure. Cleanup still has to release Step.
            }
            finally
            {
                releaseStep.Set();
            }
        }
    }

    private sealed class MachineSessionHarness : ISessionHarness
    {
        private readonly DebugContext context;
        private readonly IMachine machine;

        public MachineSessionHarness()
        {
            var dispatcher = new CommandDispatcher();
            context = new(dispatcher, TextWriter.Null, TextWriter.Null);

            (machine, var disassembler, var info) = MachineFactory.CreateDebugSystem(CreateTestProfile());
            context.AttachMachine(machine, disassembler, info);
            Session = new MachineDebugSession(context);
        }

        public IDebugSession Session { get; }

        public void LoadProgram(ushort startAddress, params byte[] bytes)
        {
            Session.WriteMemory(startAddress, bytes);
            Session.WriteMemory(0xFFFC, [(byte)(startAddress & 0xFF), (byte)(startAddress >> 8)]);
            Session.Reset(ResetKind.Warm);
        }

        public void Dispose()
        {
            machine.Stop();
            context.Dispose();
        }

        private static MachineProfile CreateTestProfile()
        {
            return new()
            {
                Name = "machine-debug-session-test",
                DisplayName = "Machine Debug Session Test",
                Cpu = new() { Type = "65C02" },
                AddressSpace = 16,
                Memory = new()
                {
                    Physical =
                    [
                        new()
                        {
                            Name = "main-ram-64k",
                            Size = "0x10000",
                            Fill = "0x00",
                        },
                    ],
                    Regions =
                    [
                        new()
                        {
                            Name = "main-ram",
                            Type = "ram",
                            Start = "0x0000",
                            Size = "0x10000",
                            Permissions = "rwx",
                            Source = "main-ram-64k",
                            SourceOffset = "0x0000",
                        },
                    ],
                },
            };
        }
    }
}