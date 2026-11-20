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
using BadMango.Emulator.Core.Debugger;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Core.Interfaces.Debugging;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.Interfaces;
using BadMango.Emulator.Rendering;
using BadMango.Emulator.Storage.Media;

using Moq;

/// <summary>
/// Contract and focused behavior tests for <see cref="MachineDebugSession"/>.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionTests : IDebugSessionContractTests
{
    /// <summary>
    /// Verifies video-state snapshots come from the attached video model.
    /// </summary>
    [Test]
    public void GetVideoState_UsesAttachedVideoDevice()
    {
        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.CurrentMode).Returns(VideoMode.Text80);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(true);
        video.SetupGet(v => v.IsHiRes).Returns(false);
        video.SetupGet(v => v.Is80Column).Returns(true);
        video.SetupGet(v => v.IsDoubleHiRes).Returns(false);
        video.SetupGet(v => v.IsVerticalBlanking).Returns(true);
        video.SetupGet(v => v.Annunciators).Returns([true, false, true, false]);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        VideoStateSnapshot state = session.GetVideoState();
        Assert.Multiple(() =>
        {
            Assert.That(state.Mode, Is.EqualTo(VideoMode.Text80.ToString()));
            Assert.That(state.Is80Column, Is.True);
            Assert.That(state.IsVerticalBlanking, Is.True);
            Assert.That(state.Annunciators, Is.EqualTo(new[] { true, false, true, false }));
        });
    }

    /// <summary>
    /// Verifies 80-column screen text is read from interleaved auxiliary and main text pages.
    /// </summary>
    [Test]
    public void GetScreenText_When80ColumnMode_InterleavesAuxAndMain()
    {
        byte[] ram = new byte[0x10000];
        var provider = new FakeMainMemoryProvider(ram);
        var aux = new Mock<IExtended80ColumnDevice>(MockBehavior.Strict);
        aux.Setup(a => a.ReadAuxRam(It.IsAny<ushort>())).Returns((ushort _) => (byte)0xC1); // 'A'

        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.Is80Column).Returns(true);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(false);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(m => m.GetComponent<IMainMemoryProvider>()).Returns(provider);
        machine.Setup(m => m.GetComponent<IExtended80ColumnDevice>()).Returns(aux.Object);
        machine.SetupGet(m => m.Bus).Returns(Mock.Of<IMemoryBus>());

        ram[0x0400] = 0xC2; // 'B'
        ram[0x0401] = 0xC2; // 'B'

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        ScreenTextSnapshot screen = session.GetScreenText();
        Assert.Multiple(() =>
        {
            Assert.That(screen.Columns, Is.EqualTo(80));
            Assert.That(screen.Lines[0].Substring(0, 4), Is.EqualTo("ABAB"));
            Assert.That(screen.Is80Column, Is.True);
        });
    }

    /// <summary>
    /// Verifies typed key requests are forwarded to keyboard input.
    /// </summary>
    [Test]
    public void TypeKeys_ForwardsToKeyboardInput()
    {
        var keyboardInput = new Mock<IKeyboardInput>(MockBehavior.Strict);
        keyboardInput
            .Setup(k => k.TypeText("HELLO", null))
            .Returns(new BadMango.Emulator.Devices.TypeTextReceipt(5, ['?'], Rejected: false));

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.Setup(m => m.GetComponent<IKeyboardInput>()).Returns(keyboardInput.Object);

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        BadMango.Emulator.Debug.Abstractions.TypeKeysReceipt receipt = session.TypeKeys("HELLO");
        Assert.Multiple(() =>
        {
            Assert.That(receipt.Accepted, Is.EqualTo(5));
            Assert.That(receipt.Skipped, Is.EqualTo(new[] { '?' }));
            Assert.That(receipt.Rejected, Is.False);
        });
    }

    /// <summary>
    /// Verifies disk listings return live controller and drive snapshot data.
    /// </summary>
    [Test]
    public void ListDisks_ReturnsControllerAndDriveSnapshots()
    {
        var card = new Mock<ISlotCard>(MockBehavior.Strict);
        card.SetupGet(c => c.Name).Returns("Disk II");

        var controller = card.As<IDiskController>();
        controller.SetupGet(c => c.DriveCount).Returns(1);
        controller.Setup(c => c.GetDriveSnapshot(0)).Returns(new DriveSnapshot(
            Selected: true,
            MotorOn: true,
            PhaseLatch: 2,
            QuarterTrack: 8,
            WriteProtect: false,
            HasMedia: true,
            MountedImagePath: "/tmp/test.dsk",
            Geometry: DiskGeometry.Standard525Dos));

        var slotManager = new Mock<ISlotManager>(MockBehavior.Loose);
        slotManager.Setup(s => s.GetCard(6)).Returns(card.Object);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.Setup(m => m.GetComponent<ISlotManager>()).Returns(slotManager.Object);

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        IReadOnlyList<DiskControllerSnapshot> disks = session.ListDisks();
        Assert.Multiple(() =>
        {
            Assert.That(disks.Count, Is.EqualTo(1));
            Assert.That(disks[0].Slot, Is.EqualTo(6));
            Assert.That(disks[0].Drives[0].MountedImagePath, Is.EqualTo("/tmp/test.dsk"));
            Assert.That(disks[0].Drives[0].Geometry!.TrackCount, Is.EqualTo(35));
        });
    }

    /// <summary>
    /// Verifies screen-text waiting advances execution until the requested pattern appears.
    /// </summary>
    [Test]
    public void WaitForScreenText_WhenPatternAppears_ReturnsTrue()
    {
        byte[] ram = new byte[0x10000];
        ram[0x0400] = 0xC1; // 'A'
        var provider = new FakeMainMemoryProvider(ram);

        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.Is80Column).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(false);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);

        var cpu = new Mock<ICpu>(MockBehavior.Strict);
        cpu.SetupGet(c => c.Halted).Returns(false);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.SetupGet(m => m.Cpu).Returns(cpu.Object);
        machine.SetupGet(m => m.Bus).Returns(Mock.Of<IMemoryBus>());
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(m => m.GetComponent<IMainMemoryProvider>()).Returns(provider);
        machine.Setup(m => m.GetComponent<IExtended80ColumnDevice>()).Returns((IExtended80ColumnDevice?)null);
        machine.Setup(m => m.Step()).Returns(() =>
        {
            ram[0x0400] = 0xDD; // ']'
            return new CpuStepResult(CpuRunState.Running, new Cycle(2));
        });

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        ScreenTextWaitResult matched = session.WaitForScreenText(@"\]", new RunBudget(MaxCycles: 20));
        Assert.That(matched.Reason, Is.EqualTo(ScreenTextWaitReason.Matched));
        Assert.That(matched.Matched, Is.True);
    }

    /// <summary>
    /// Verifies a 40-column page, including page 2 and inverse and flashing glyphs.
    /// </summary>
    [Test]
    public void GetScreenText_Reads40ColumnPage2InverseAndFlashingGlyphs()
    {
        byte[] ram = new byte[0x10000];
        ram[0x0400] = 0xC2; // normal 'B' on page 1
        ram[0x0800] = 0x01; // inverse 'A' on page 2
        ram[0x0801] = 0x41; // flashing 'A' on page 2
        var provider = new FakeMainMemoryProvider(ram);

        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.Is80Column).Returns(false);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(true);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(m => m.GetComponent<IMainMemoryProvider>()).Returns(provider);
        machine.Setup(m => m.GetComponent<IExtended80ColumnDevice>()).Returns((IExtended80ColumnDevice?)null);
        machine.SetupGet(m => m.Bus).Returns(Mock.Of<IMemoryBus>());

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        ScreenTextSnapshot screen = session.GetScreenText();
        Assert.Multiple(() =>
        {
            Assert.That(screen.Columns, Is.EqualTo(40));
            Assert.That(screen.Lines[0], Has.Length.EqualTo(40));
            Assert.That(screen.IsPage2, Is.True);
            Assert.That(TextScreen.IsInverse(0x01), Is.True);
            Assert.That(TextScreen.IsFlashing(0x41), Is.True);
            Assert.That(screen.Lines[0][..2], Is.EqualTo("AA"));
            Assert.That(screen.CombinedText, Is.EqualTo(string.Join("\n", screen.Lines)));
            Assert.That(screen.CombinedText, Does.Not.Contain("\r"));
        });
    }

    /// <summary>
    /// Verifies the screen is sampled about once per frame, not after every instruction.
    /// </summary>
    [Test]
    public void WaitForScreenText_PollsOncePerFrame()
    {
        int reads = 0;
        byte[] ram = new byte[0x10000];
        var provider = new CountingMainMemoryProvider(ram, () => reads++);

        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.Is80Column).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(false);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);

        int steps = 0;
        var cpu = new Mock<ICpu>(MockBehavior.Strict);
        cpu.SetupGet(c => c.Halted).Returns(false);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.SetupGet(m => m.Cpu).Returns(cpu.Object);
        machine.SetupGet(m => m.Bus).Returns(Mock.Of<IMemoryBus>());
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(m => m.GetComponent<IMainMemoryProvider>()).Returns(provider);
        machine.Setup(m => m.GetComponent<IExtended80ColumnDevice>()).Returns((IExtended80ColumnDevice?)null);
        machine.Setup(m => m.Step()).Returns(() =>
        {
            steps++;
            return new CpuStepResult(CpuRunState.Running, new Cycle(1));
        });

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);

        var session = new MachineDebugSession(context.Object);
        ScreenTextWaitResult result = session.WaitForScreenText(
            "NO-SUCH-TEXT",
            new RunBudget(MaxCycles: MachineDebugSession.ScreenTextPollIntervalCycles));

        Assert.Multiple(() =>
        {
            Assert.That(result.Reason, Is.EqualTo(ScreenTextWaitReason.BudgetExhausted));
            Assert.That(steps, Is.EqualTo((int)MachineDebugSession.ScreenTextPollIntervalCycles));
            Assert.That(reads, Is.LessThan(4_000));
            Assert.That(reads, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// Verifies each non-match stop reason is reported instead of a bare false.
    /// </summary>
    [Test]
    public void WaitForScreenText_ReportsEveryStopReason()
    {
        byte[] ram = new byte[0x10000];
        var provider = new FakeMainMemoryProvider(ram);
        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(v => v.Is80Column).Returns(false);
        video.SetupGet(v => v.IsPage2).Returns(false);
        video.SetupGet(v => v.IsTextMode).Returns(true);
        video.SetupGet(v => v.IsMixedMode).Returns(false);

        var haltedCpu = new Mock<ICpu>(MockBehavior.Strict);
        haltedCpu.SetupGet(c => c.Halted).Returns(true);
        var runningCpu = new Mock<ICpu>(MockBehavior.Strict);
        runningCpu.SetupGet(c => c.Halted).Returns(false);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.SetupGet(m => m.Bus).Returns(Mock.Of<IMemoryBus>());
        machine.Setup(m => m.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(m => m.GetComponent<IMainMemoryProvider>()).Returns(provider);
        machine.Setup(m => m.GetComponent<IExtended80ColumnDevice>()).Returns((IExtended80ColumnDevice?)null);
        machine.Setup(m => m.Step()).Throws(new InvalidOperationException("step failed"));

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(c => c.Machine).Returns(machine.Object);
        var session = new MachineDebugSession(context.Object);

        machine.SetupGet(m => m.Cpu).Returns(haltedCpu.Object);
        ScreenTextWaitResult halted = session.WaitForScreenText("NO-SUCH-TEXT", new RunBudget(MaxCycles: 100));

        machine.SetupGet(m => m.Cpu).Returns(runningCpu.Object);
        ScreenTextWaitResult budget = session.WaitForScreenText("NO-SUCH-TEXT", new RunBudget(MaxCycles: 0));
        ScreenTextWaitResult wall = session.WaitForScreenText(
            "NO-SUCH-TEXT",
            new RunBudget(MaxCycles: 100, WallClock: TimeSpan.Zero));
        ScreenTextWaitResult cancelled = session.WaitForScreenText(
            "NO-SUCH-TEXT",
            new RunBudget(MaxCycles: 100),
            new CancellationToken(canceled: true));
        ScreenTextWaitResult faulted = session.WaitForScreenText("NO-SUCH-TEXT", new RunBudget(MaxCycles: 10));

        Assert.Multiple(() =>
        {
            Assert.That(halted.Reason, Is.EqualTo(ScreenTextWaitReason.Halted));
            Assert.That(budget.Reason, Is.EqualTo(ScreenTextWaitReason.BudgetExhausted));
            Assert.That(wall.Reason, Is.EqualTo(ScreenTextWaitReason.WallClockTimeout));
            Assert.That(cancelled.Reason, Is.EqualTo(ScreenTextWaitReason.Cancelled));
            Assert.That(faulted.Reason, Is.EqualTo(ScreenTextWaitReason.Faulted));
            Assert.That(faulted.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(faulted.FaultMessage, Does.Contain("step failed"));
        });
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
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
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
    /// Verifies a reattached machine is the one the session reads and writes.
    /// </summary>
    [Test]
    public void Session_AfterMachineReattached_DrivesTheNewMachine()
    {
        (IMachine first, IMemoryBus firstBus) = CreateRamMachine();
        (IMachine second, IMemoryBus secondBus) = CreateRamMachine();
        IMachine? current = first;

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Machine).Returns(() => current);
        var session = new MachineDebugSession(context.Object);

        session.WriteMemory(0x0200, [0x11]);
        current = second;
        session.WriteMemory(0x0200, [0x22]);

        Assert.Multiple(() =>
        {
            Assert.That(ReadBusByte(firstBus, 0x0200), Is.EqualTo(0x11));
            Assert.That(ReadBusByte(secondBus, 0x0200), Is.EqualTo(0x22));
        });
    }

    /// <summary>
    /// Verifies a detached machine is not used after the context clears it.
    /// </summary>
    [Test]
    public void Session_WhenMachineDetached_Throws()
    {
        (IMachine first, IMemoryBus _) = CreateRamMachine();
        IMachine? current = first;
        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Machine).Returns(() => current);
        var session = new MachineDebugSession(context.Object);

        current = null;
        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => session.ReadMemory(0x0200, 1));
        Assert.That(error!.Message, Does.Contain("No machine"));
    }

    /// <summary>
    /// Verifies replacing the machine ends a background run instead of stepping the old one.
    /// </summary>
    [Test]
    public void RunUntil_WhenAttachedMachineIsReplaced_StopsWithoutFurtherSteps()
    {
        int steps = 0;
        var releaseStep = new ManualResetEventSlim(false);
        var stepEntered = new ManualResetEventSlim(false);
        MachineDebugSession? session = null;
        Guid runId = Guid.Empty;
        try
        {
            var cpu = new Mock<ICpu>(MockBehavior.Strict);
            cpu.SetupGet(x => x.Halted).Returns(false);
            cpu.Setup(x => x.GetPC()).Returns(0x1000u);

            var machine = new Mock<IMachine>(MockBehavior.Strict);
            machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
            machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
            machine.Setup(x => x.Pause());
            machine.Setup(x => x.Step()).Returns(() =>
            {
                int n = Interlocked.Increment(ref steps);
                if (n == 1)
                {
                    stepEntered.Set();
                    Assert.That(releaseStep.Wait(TimeSpan.FromSeconds(3)), Is.True);
                }

                return new CpuStepResult(CpuRunState.Running, new Cycle(2));
            });

            (IMachine replacement, IMemoryBus _) = CreateRamMachine();
            IMachine current = machine.Object;
            var context = new Mock<IDebugContext>(MockBehavior.Strict);
            context.SetupGet(x => x.Machine).Returns(() => current);
            context.SetupGet(x => x.Breakpoints).Returns(new BreakpointManager());
            context.SetupGet(x => x.Watchpoints).Returns(new WatchpointManager());
            session = new MachineDebugSession(context.Object);

            runId = session.StartRun(
                new RunCondition(RunConditionKind.ProgramCounter, Address: 0x2000),
                new RunBudget(MaxCycles: ulong.MaxValue));
            Assert.That(stepEntered.Wait(TimeSpan.FromSeconds(2)), Is.True);
            current = replacement;
            releaseStep.Set();

            RunResult? result = WaitForBackgroundResult(session, runId);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                if (result is not null)
                {
                    Assert.That(result.StopReason, Is.EqualTo(StopReason.Cancelled));
                }

                Assert.That(steps, Is.EqualTo(1));
            });
        }
        finally
        {
            releaseStep.Set();
            if (session is not null && runId != Guid.Empty)
            {
                session.StopRun(runId);
            }

            releaseStep.Dispose();
            stepEntered.Dispose();
        }
    }

    /// <summary>
    /// Verifies stepping is rejected while a background run owns execution.
    /// </summary>
    [Test]
    public void Step_DuringBackgroundRun_IsRejected()
    {
        using var run = BlockedBackgroundRun.Start();
        var step = Task.Run(() => run.Session.Step());
        (bool completed, Exception? error) = WaitForTask(step, TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True, "Step blocked while a background run owned execution.");
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            if (error is not null)
            {
                Assert.That(error.Message, Does.Contain("background run").IgnoreCase);
            }
        });
    }

    /// <summary>
    /// Verifies run-until is rejected while a background run owns execution.
    /// </summary>
    [Test]
    public void RunUntil_DuringBackgroundRun_IsRejected()
    {
        using var run = BlockedBackgroundRun.Start();
        var runUntil = Task.Run(() => run.Session.RunUntil(
            new RunCondition(RunConditionKind.ProgramCounter, Address: 0x1000),
            new RunBudget(MaxCycles: 10)));
        (bool completed, Exception? error) = WaitForTask(runUntil, TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True, "RunUntil blocked while a background run owned execution.");
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            if (error is not null)
            {
                Assert.That(error.Message, Does.Contain("background run").IgnoreCase);
            }
        });
    }

    /// <summary>
    /// Verifies memory can be written as soon as stopping a background run returns.
    /// </summary>
    [Test]
    public void StopRun_WhenItReturns_AllowsImmediateWrite()
    {
        using var run = BlockedBackgroundRun.Start();
        var stop = Task.Run(() => run.Session.StopRun(run.RunId));
        bool returnedWhileStepBlocked = stop.Wait(TimeSpan.FromMilliseconds(200));
        run.ReleaseStep();
        (bool completed, Exception? error) = WaitForTask(stop, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(returnedWhileStepBlocked, Is.False, "StopRun returned while the run was still inside Step.");
            Assert.That(completed, Is.True);
            Assert.That(error, Is.Null);
        });

        Assert.DoesNotThrow(() => run.Session.WriteMemory(0x0300, [0x5A]));
        Assert.That(run.BusWrites, Is.EqualTo(1));
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
        run.ReleaseStep();
        Assert.That(run.Session.StopRun(run.RunId), Is.True);

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
            machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
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
            machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
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

    private static (IMachine Machine, IMemoryBus Bus) CreateRamMachine()
    {
        var memory = new PhysicalMemory(0x10000, "session-ram");
        var target = new RamTarget(memory.Slice(0, 0x10000));
        var bus = new MainBus();
        for (int page = 0; page < 16; page++)
        {
            bus.MapPage(
                page,
                new PageEntry(1, RegionTag.Ram, PagePerms.All, TargetCaps.None, target, (uint)(page << 12)));
        }

        var cpu = new Mock<ICpu>(MockBehavior.Loose);
        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
        machine.SetupGet(x => x.Bus).Returns(bus);
        machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
        return (machine.Object, bus);
    }

    private static byte ReadBusByte(IMemoryBus bus, uint address)
    {
        var access = new BusAccess(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects);
        BusResult<byte> read = bus.TryRead8(access);
        Assert.That(read.Ok, Is.True, $"Bus read at ${address:X4} failed.");
        return read.Value;
    }

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
            machine.SetupGet(x => x.State).Returns(MachineState.Stopped);
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
            releaseStep.Set();
            if (RunId != Guid.Empty)
            {
                Session.StopRun(RunId);
            }
        }
    }

    private sealed class FakeMainMemoryProvider(byte[] memory) : IMainMemoryProvider
    {
        public ReadOnlyMemory<byte> MainRam => memory;

        public byte ReadMainRam(ushort address) => memory[address];
    }

    private sealed class CountingMainMemoryProvider(byte[] memory, Action onRead) : IMainMemoryProvider
    {
        public ReadOnlyMemory<byte> MainRam => memory;

        public byte ReadMainRam(ushort address)
        {
            onRead();
            return memory[address];
        }
    }

    private sealed class ThrowingStepListener : IDebugStepListener
    {
        public void OnBeforeStep(in DebugStepEventArgs eventData) =>
            throw new InvalidOperationException("simulated execution fault");

        public void OnAfterStep(in DebugStepEventArgs eventData)
        {
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
            machine.PowerOnAsync().GetAwaiter().GetResult();
            context.AttachMachine(machine, disassembler, info);
            Session = new MachineDebugSession(context);
        }

        public IDebugSession Session { get; }

        public bool IsMachineStopped => machine.State == MachineState.Stopped;

        public void LoadProgram(ushort startAddress, params byte[] bytes)
        {
            Session.WriteMemory(startAddress, bytes);
            Session.WriteMemory(0xFFFC, [(byte)(startAddress & 0xFF), (byte)(startAddress >> 8)]);
            Session.Reset(ResetKind.Warm);
        }

        public void ArmExecutionFault()
        {
            context.StepListener!.Add(new ThrowingStepListener());
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