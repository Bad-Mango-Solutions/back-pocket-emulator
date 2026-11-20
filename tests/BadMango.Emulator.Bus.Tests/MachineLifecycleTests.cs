// <copyright file="MachineLifecycleTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using System.Reflection;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Interfaces;

using Moq;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Machine disposal order and post-initialization component factories.
/// </summary>
[TestFixture]
public class MachineLifecycleTests
{
    /// <summary>
    /// Disposable components, including one created by <see cref="MachineBuilder.AddComponentFactory{T}"/>,
    /// are disposed in reverse registration order, exactly once. A second <see cref="IDisposable.Dispose"/>
    /// does nothing.
    /// </summary>
    [Test]
    public void Dispose_DisposesComponentsInReverseRegistrationOrder_ExactlyOnce()
    {
        var order = new List<string>();
        var first = new RecordingDisposable("first", order);
        var second = new RecordingDisposable("second", order);
        var third = new RecordingDisposable("third", order);
        IMachine machine = new MachineBuilder()
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddComponent(first)
            .AddComponent(second)
            .AddComponentFactory<RecordingDisposable>(_ => third)
            .Build();

        machine.Dispose();
        machine.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "third", "second", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A component that throws <see cref="ObjectDisposedException"/> or <see cref="InvalidOperationException"/>
    /// during disposal is logged, and the remaining components still dispose exactly once.
    /// </summary>
    [Test]
    public void Dispose_LogsComponentDisposalFailures_AndContinues()
    {
        var order = new List<string>();
        var sink = new CollectingSink();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var first = new RecordingDisposable("first", order);
        var throwsDisposed = new ThrowingDisposable("throws-disposed", order, new ObjectDisposedException("throws-disposed"));
        var throwsInvalid = new ThrowingDisposable("throws-invalid", order, new InvalidOperationException("throws-invalid"));
        var last = new RecordingDisposable("last", order);
        IMachine machine = new MachineBuilder()
            .WithLogger(logger)
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddComponent(first)
            .AddComponent(throwsDisposed)
            .AddComponent(throwsInvalid)
            .AddComponent(last)
            .Build();

        machine.Dispose();
        machine.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "last", "throws-invalid", "throws-disposed", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(throwsDisposed.DisposeCount, Is.EqualTo(1));
            Assert.That(throwsInvalid.DisposeCount, Is.EqualTo(1));
            Assert.That(last.DisposeCount, Is.EqualTo(1));
            Assert.That(sink.Events, Has.Count.EqualTo(2));
            Assert.That(sink.Events.Select(logEvent => logEvent.Level), Is.All.EqualTo(LogEventLevel.Warning));
            Assert.That(
                sink.Events.Select(logEvent => logEvent.Exception),
                Has.Some.InstanceOf<ObjectDisposedException>());
            Assert.That(
                sink.Events.Select(logEvent => logEvent.Exception),
                Has.Some.InstanceOf<InvalidOperationException>());
        });
    }

    /// <summary>
    /// <see cref="MachineBuilder.AddComponentFactory{T}"/> runs after device initialization, receives the
    /// built machine, and the result is retrievable as a component.
    /// </summary>
    [Test]
    public void AddComponentFactory_RunsAfterDeviceInitialization_AndComponentIsRetrievable()
    {
        var probe = new ProbeDevice();
        Marker? created = null;
        IMachine? seen = null;
        bool initializedWhenFactoryRan = false;

        IMachine machine = new MachineBuilder()
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddDevice(probe)
            .AddComponentFactory<Marker>(built =>
            {
                initializedWhenFactoryRan = probe.Initialized;
                seen = built;
                created = new Marker();
                return created;
            })
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(initializedWhenFactoryRan, Is.True);
            Assert.That(seen, Is.SameAs(machine));
            Assert.That(created, Is.Not.Null);
            Assert.That(machine.GetComponent<Marker>(), Is.SameAs(created));
        });

        machine.Dispose();
    }

    /// <summary>
    /// A component that throws <see cref="NotSupportedException"/> does not stop disposal of the others,
    /// and each of them still runs exactly once.
    /// </summary>
    [Test]
    public void Dispose_WhenComponentThrowsNotSupportedException_DisposesTheOthersOnce()
    {
        var order = new List<string>();
        var sink = new CollectingSink();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var first = new RecordingDisposable("first", order);
        var throwsUnsupported = new ThrowingDisposable(
            "throws-unsupported",
            order,
            new NotSupportedException("throws-unsupported"));
        var last = new RecordingDisposable("last", order);
        IMachine machine = new MachineBuilder()
            .WithLogger(logger)
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddComponent(first)
            .AddComponent(throwsUnsupported)
            .AddComponent(last)
            .Build();

        Assert.That(() => machine.Dispose(), Throws.Nothing);
        machine.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "last", "throws-unsupported", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(throwsUnsupported.DisposeCount, Is.EqualTo(1));
            Assert.That(last.DisposeCount, Is.EqualTo(1));
            bool loggedUnsupported = false;
            foreach (LogEvent logEvent in sink.Events)
            {
                if (logEvent.Level == LogEventLevel.Warning && logEvent.Exception is NotSupportedException)
                {
                    loggedUnsupported = true;
                }
            }

            Assert.That(loggedUnsupported, Is.True);
        });
    }

    /// <summary>
    /// Dispose waits for an active <see cref="IMachine.Run"/> to leave its loop
    /// before the first component is disposed.
    /// </summary>
    [Test]
    public void Dispose_WhileRunIsActive_ExitsTheLoopBeforeDisposingComponents()
    {
        var order = new List<string>();
        int inStep = 0;
        int stopRequested = 0;
        using var entered = new ManualResetEventSlim(false);
        int generationAtFirstDispose = -1;
        int inStepAtFirstDispose = -1;

        // Power-on steps the CPU for the reset hold. That must return; the blocking
        // step is only the run loop the test disposes.
        int blockSteps = 0;
        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(() => Volatile.Read(ref stopRequested) != 0);
        cpu.Setup(mock => mock.RequestStop()).Callback(() => Volatile.Write(ref stopRequested, 1));
        cpu.Setup(mock => mock.ClearStopRequest()).Callback(() => Volatile.Write(ref stopRequested, 0));
        cpu.Setup(mock => mock.Step()).Returns(() =>
        {
            if (Volatile.Read(ref blockSteps) == 0)
            {
                return new CpuStepResult(CpuRunState.Running, 1);
            }

            Interlocked.Increment(ref inStep);
            entered.Set();
            while (Volatile.Read(ref stopRequested) == 0)
            {
                Thread.Sleep(1);
            }

            Interlocked.Decrement(ref inStep);
            return new CpuStepResult(CpuRunState.Running, 1);
        });

        var trailing = new RecordingDisposable("trailing", order);
        IMachine? built = null;
        built = new MachineBuilder()
            .WithCpuFactory(_ => cpu.Object)
            .AddComponent(trailing)
            .AddComponent(new ObservingDisposable("first-disposed", order, () =>
            {
                generationAtFirstDispose = ActiveLoopGeneration(built!);
                inStepAtFirstDispose = Volatile.Read(ref inStep);
            }))
            .Build();
        IMachine machine = built!;
        machine.PowerOnAsync().GetAwaiter().GetResult();
        Volatile.Write(ref blockSteps, 1);

        var runThread = new Thread(() => machine.Run())
        {
            IsBackground = true,
            Name = "lifecycle-run",
        };
        runThread.Start();
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True, "The run loop never stepped the CPU.");

        machine.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(runThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Run did not return after Dispose.");
            Assert.That(generationAtFirstDispose, Is.EqualTo(0));
            Assert.That(inStepAtFirstDispose, Is.EqualTo(0));
            Assert.That(order, Is.EqualTo(new[] { "first-disposed", "trailing" }));
            Assert.That(trailing.DisposeCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Run, Step, and Resume throw <see cref="ObjectDisposedException"/> after the machine is disposed.
    /// </summary>
    [Test]
    public void Run_Step_AndResume_AfterDispose_ThrowObjectDisposedException()
    {
        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(false);
        cpu.Setup(mock => mock.Step()).Returns(new CpuStepResult(CpuRunState.Stopped, 1));

        IMachine machine = new MachineBuilder()
            .WithCpuFactory(_ => cpu.Object)
            .Build();

        machine.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => machine.Run(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => machine.Step(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                async () => await machine.ResumeAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    /// <summary>
    /// Powering off a running machine from a thread that is not the machine thread returns
    /// while the run loop is still inside the CPU.
    /// </summary>
    [Test]
    public void PowerOff_WhileRunningFromAnotherThread_Completes()
    {
        int stopRequested = 0;
        int observeRun = 0;
        using var entered = new ManualResetEventSlim(false);
        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(() => Volatile.Read(ref stopRequested) != 0);
        cpu.Setup(mock => mock.RequestStop()).Callback(() => Volatile.Write(ref stopRequested, 1));
        cpu.Setup(mock => mock.ClearStopRequest()).Callback(() => Volatile.Write(ref stopRequested, 0));
        cpu.Setup(mock => mock.Step()).Returns(() =>
        {
            if (Volatile.Read(ref observeRun) != 0)
            {
                entered.Set();
            }

            return new CpuStepResult(CpuRunState.Running, 1);
        });

        IMachine machine = new MachineBuilder()
            .WithCpuFactory(_ => cpu.Object)
            .Build();
        machine.PowerOnAsync().GetAwaiter().GetResult();
        Volatile.Write(ref observeRun, 1);

        var runThread = new Thread(() => machine.Run())
        {
            IsBackground = true,
            Name = "power-off-run",
        };
        runThread.Start();
        Assume.That(
            entered.Wait(TimeSpan.FromSeconds(2)),
            Is.True,
            "The run loop never stepped the CPU.");
        Assume.That(machine.State, Is.EqualTo(MachineState.Running));

        var powerOff = Task.Run(() => machine.PowerOffAsync().GetAwaiter().GetResult());
        bool completed = powerOff.Wait(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True, "PowerOffAsync did not return while the machine was running.");
            Assert.That(powerOff.IsCompletedSuccessfully, Is.True);
            Assert.That(machine.State, Is.EqualTo(MachineState.Off));
            Assert.That(runThread.Join(TimeSpan.FromSeconds(2)), Is.True, "The run loop did not exit after power-off.");
        });
    }

    /// <summary>
    /// Power-off flushes every drive on component and slot-card controllers, leaves the
    /// media mounted, and lets an <see cref="IOException"/> out without wrapping it.
    /// </summary>
    [Test]
    public void PowerOff_FlushesStorage_AndSurfacesIOExceptionUnwrapped()
    {
        var component = new RecordingStorage(driveCount: 2);
        var cardFlushed = new List<int>();
        var card = new Mock<ISlotCard>();
        card.SetupProperty(mock => mock.SlotNumber);
        card.Setup(mock => mock.IOHandlers).Returns((SlotIOHandlers?)null);
        var flushable = card.As<IFlushableStorage>();
        flushable.SetupGet(mock => mock.DriveCount).Returns(1);
        flushable.Setup(mock => mock.Flush(It.IsAny<int>())).Callback<int>(cardFlushed.Add);
        var slotManager = new SlotManager(new IOPageDispatcher());
        slotManager.Install(6, card.Object);

        var machine = new MachineBuilder()
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddComponent(component)
            .Build();
        ((Machine)machine).AddComponent<ISlotManager>(slotManager);
        machine.PowerOnAsync().GetAwaiter().GetResult();

        machine.PowerOffAsync().GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(machine.State, Is.EqualTo(MachineState.Off));
            Assert.That(component.Flushed, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(cardFlushed, Is.EqualTo(new[] { 0 }));
            Assert.That(component.Mounted, Is.True);
        });

        component.Error = new IOException("disk full");
        machine.PowerOnAsync().GetAwaiter().GetResult();
        var io = Assert.Throws<IOException>(() => machine.PowerOffAsync().GetAwaiter().GetResult());
        Assert.Multiple(() =>
        {
            Assert.That(io!.Message, Is.EqualTo("disk full"));
            Assert.That(machine.State, Is.EqualTo(MachineState.Stopped));
            Assert.That(component.Mounted, Is.True);
        });

        component.Error = new UnauthorizedAccessException("denied");
        var denied = Assert.Throws<UnauthorizedAccessException>(() => machine.PowerOffAsync().GetAwaiter().GetResult());
        Assert.That(denied!.Message, Is.EqualTo("denied"));
    }

    /// <summary>
    /// An omitted power-on seed is taken from the host clock and logged, on both power-on
    /// and power-off. A fixed seed is applied as given.
    /// </summary>
    [Test]
    public void PowerCycle_AppliesHostClockSeedWhenSeedIsOmitted()
    {
        var sink = new CollectingSink();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var ram = new RecordingRam();
        IMachine machine = new MachineBuilder()
            .WithLogger(logger)
            .WithCpuFactory(_ => CreateCpu().Object)
            .AddComponent(ram)
            .Build();

        machine.PowerOnMemorySeed = 99;
        machine.PowerOnAsync().GetAwaiter().GetResult();
        Assert.That(ram.AppliedSeed, Is.EqualTo(99));
        Assert.That(sink.Events, Is.Empty);

        machine.PowerOnMemorySeed = null;
        machine.PowerOffAsync().GetAwaiter().GetResult();
        Assert.That(LoggedSeed(sink), Is.EqualTo(ram.AppliedSeed));

        sink.Events.Clear();
        machine.PowerOnAsync().GetAwaiter().GetResult();
        Assert.That(LoggedSeed(sink), Is.EqualTo(ram.AppliedSeed));
    }

    /// <summary>
    /// Inserting and removing a slot card while off installs handlers, initializes once,
    /// registers/unregisters device metadata, flushes storage, and disposes the card.
    /// </summary>
    [Test]
    public void InsertRemoveSlotCard_WhileOff_InstallsAndRemovesCard()
    {
        var dispatcher = new IOPageDispatcher();
        var slotManager = new SlotManager(dispatcher);
        var card = new RuntimeSlotCard();
        var machine = new MachineBuilder()
            .WithCpuFactory(_ => CreateCpu().Object)
            .Build();
        ((Machine)machine).AddComponent<ISlotManager>(slotManager);

        machine.InsertSlotCard(5, card);

        var access = new BusAccess(
            Address: 0xC0D0,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects);

        var registered = machine.Devices.GetAll()
            .SingleOrDefault(device => device.WiringPath == "Slot/5/runtime-slot-card");

        Assert.Multiple(() =>
        {
            Assert.That(slotManager.GetCard(5), Is.SameAs(card));
            Assert.That(card.InitializeCount, Is.EqualTo(1));
            Assert.That(dispatcher.Read(0xD0, in access), Is.EqualTo((byte)0x5A));
            Assert.That(registered.WiringPath, Is.EqualTo("Slot/5/runtime-slot-card"));
            Assert.That(registered.Name, Is.EqualTo(card.Name));
        });

        machine.RemoveSlotCard(5);
        Assert.Multiple(() =>
        {
            Assert.That(slotManager.GetCard(5), Is.Null);
            Assert.That(dispatcher.Read(0xD0, in access), Is.EqualTo((byte)0xFF));
            Assert.That(machine.Devices.GetAll().Any(device => device.WiringPath == "Slot/5/runtime-slot-card"), Is.False);
            Assert.That(card.FlushCount, Is.EqualTo(1));
            Assert.That(card.DisposeCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Slot mutations are rejected unless the machine is powered off.
    /// </summary>
    [Test]
    public void InsertRemoveSlotCard_WhenNotOff_ThrowsInvalidOperationException()
    {
        var slotManager = new SlotManager(new IOPageDispatcher());
        var machine = new MachineBuilder()
            .WithCpuFactory(_ => CreateCpu().Object)
            .Build();
        ((Machine)machine).AddComponent<ISlotManager>(slotManager);
        machine.PowerOnAsync().GetAwaiter().GetResult();

        var insert = Assert.Throws<InvalidOperationException>(() => machine.InsertSlotCard(4, new RuntimeSlotCard()));
        var remove = Assert.Throws<InvalidOperationException>(() => machine.RemoveSlotCard(4));

        Assert.Multiple(() =>
        {
            Assert.That(insert!.Message, Is.EqualTo("Slot changes require the machine to be off."));
            Assert.That(remove!.Message, Is.EqualTo("Slot changes require the machine to be off."));
        });
    }

    private static int LoggedSeed(CollectingSink sink)
    {
        LogEvent? logged = null;
        foreach (LogEvent logEvent in sink.Events)
        {
            if (logEvent.Level == LogEventLevel.Information
                && logEvent.Properties.ContainsKey("Seed"))
            {
                logged = logEvent;
            }
        }

        Assert.That(logged, Is.Not.Null, "A derived power-on seed was not logged.");
        return (int)((ScalarValue)logged!.Properties["Seed"]).Value!;
    }

    private static int ActiveLoopGeneration(IMachine machine)
    {
        FieldInfo? field = machine.GetType().GetField(
            "activeLoopGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        object? value = field.GetValue(machine);
        Assert.That(value, Is.Not.Null);
        return (int)value!;
    }

    private static Mock<ICpu> CreateCpu()
    {
        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(false);
        cpu.Setup(mock => mock.Step()).Returns(new CpuStepResult(CpuRunState.Running, 1));
        return cpu;
    }

    private sealed class RecordingDisposable : IDisposable
    {
        private readonly List<string> order;

        public RecordingDisposable(string name, List<string> order)
        {
            Name = name;
            this.order = order;
        }

        public string Name { get; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            order.Add(Name);
        }
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        private readonly List<string> order;
        private readonly Exception exception;

        public ThrowingDisposable(string name, List<string> order, Exception exception)
        {
            Name = name;
            this.order = order;
            this.exception = exception;
        }

        public string Name { get; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            order.Add(Name);
            throw exception;
        }
    }

    private sealed class ProbeDevice : IScheduledDevice
    {
        public string Name => "Probe";

        public bool Initialized { get; private set; }

        public void Initialize(IEventContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Initialized = true;
        }
    }

    private sealed class Marker
    {
    }

    private sealed class RecordingStorage : IFlushableStorage
    {
        public RecordingStorage(int driveCount)
        {
            DriveCount = driveCount;
        }

        public int DriveCount { get; }

        public bool Mounted { get; private set; } = true;

        public Exception? Error { get; set; }

        public List<int> Flushed { get; } = [];

        public void Flush(int driveIndex)
        {
            if (Error is not null)
            {
                throw Error;
            }

            Flushed.Add(driveIndex);
        }
    }

    private sealed class RecordingRam : IHardResetMemory
    {
        public int AppliedSeed { get; private set; }

        public void ClearForHardReset()
        {
        }

        public void ApplyPowerOnPattern(PowerOnMemoryPattern pattern)
        {
            AppliedSeed = pattern.Seed;
        }
    }

    private sealed class ObservingDisposable : IDisposable
    {
        private readonly List<string> order;
        private readonly Action observe;

        public ObservingDisposable(string name, List<string> order, Action observe)
        {
            Name = name;
            this.order = order;
            this.observe = observe;
        }

        public string Name { get; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            order.Add(Name);
            observe();
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            Events.Add(logEvent);
        }
    }

    private sealed class RuntimeSlotCard : ISlotCard, IDisposable, IFlushableStorage
    {
        public RuntimeSlotCard()
        {
            var handlers = new SlotIOHandlers();
            for (byte offset = 0; offset < 16; offset++)
            {
                handlers.Set(
                    offset,
                    (byte _, in BusAccess _) => 0x5A,
                    (byte _, byte _, in BusAccess _) => { });
            }

            IOHandlers = handlers;
        }

        public int SlotNumber { get; set; }

        public SlotIOHandlers IOHandlers { get; }

        public IBusTarget? ROMRegion => null;

        public IBusTarget? ExpansionROMRegion => null;

        public string Name => "Runtime Slot Card";

        public string DeviceType => "runtime-slot-card";

        public PeripheralKind Kind => PeripheralKind.SlotCard;

        public int DriveCount => 1;

        public int InitializeCount { get; private set; }

        public int FlushCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Initialize(IEventContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            InitializeCount++;
        }

        public void Reset()
        {
        }

        public void OnExpansionROMSelected()
        {
        }

        public void OnExpansionROMDeselected()
        {
        }

        public void Flush(int driveIndex)
        {
            if (driveIndex != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(driveIndex));
            }

            FlushCount++;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}