// <copyright file="MachineLifecycleTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

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