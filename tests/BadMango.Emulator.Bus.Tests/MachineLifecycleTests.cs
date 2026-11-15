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

        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(() => Volatile.Read(ref stopRequested) != 0);
        cpu.Setup(mock => mock.RequestStop()).Callback(() => Volatile.Write(ref stopRequested, 1));
        cpu.Setup(mock => mock.ClearStopRequest()).Callback(() => Volatile.Write(ref stopRequested, 0));
        cpu.Setup(mock => mock.Step()).Returns(() =>
        {
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
}