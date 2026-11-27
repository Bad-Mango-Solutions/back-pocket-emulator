// <copyright file="LoopbackHostSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// In-process host. Client threads call the planes. The machine gate is taken once per call, at this edge, and never from a CPU loop.
/// </summary>
public sealed partial class LoopbackHostSession : IHostSession, IHostControl, IHostPresentation, IHostInput, IHostDebugger, ILoopbackMachineEdge
{
    private readonly Lock tableLock = new();

    private readonly List<LoopbackMachine> machines = [];

    private readonly Dictionary<string, LoopbackMachine> machinesById = [];

    private readonly Dictionary<string, HostProfileDocument> profiles = [];

    private readonly List<string> profileOrder = [];

    private readonly List<IHostNotificationSink> sinks = [];

    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackHostSession"/> class.
    /// </summary>
    internal LoopbackHostSession()
    {
    }

    /// <inheritdoc />
    public int ApiVersion => HostApi.Version;

    /// <inheritdoc />
    public HostTransportInfo Transport { get; } = new(
        HostTransportKind.Loopback,
        "loopback",
        HostApi.Version,
        true,
        "Loopback · healthy");

    /// <inheritdoc />
    public IHostControl Control => this;

    /// <inheritdoc />
    public IHostPresentation Presentation => this;

    /// <inheritdoc />
    public IHostInput Input => this;

    /// <inheritdoc />
    public IHostDebugger Debugger => this;

    /// <inheritdoc />
    public void Dispose()
    {
        LoopbackMachine[] snapshot;
        lock (tableLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            snapshot = machines.ToArray();
            machines.Clear();
            machinesById.Clear();
            sinks.Clear();
        }

        foreach (LoopbackMachine machine in snapshot)
        {
            lock (machine.Gate)
            {
                machine.Removed = true;
                foreach (LoopbackSubscription subscription in machine.Subscriptions)
                {
                    subscription.MarkDisposed();
                }

                machine.Subscriptions.Clear();
            }
        }
    }

    private static void EnsureLive(LoopbackMachine machine)
    {
        if (machine.Removed)
        {
            throw Unknown(machine.Id);
        }
    }

    private static void EnsureSubscription(LoopbackSubscription subscription)
    {
        if (subscription.IsDisposed || subscription.Machine.Removed)
        {
            throw new ObjectDisposedException(nameof(IPresentationSubscription));
        }
    }

    private static InvalidOperationException Unknown(MachineId machineId)
    {
        return new InvalidOperationException($"No machine '{machineId.Value}' is on this host.");
    }

    private static Task Run(CancellationToken cancellationToken, Action action)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
        catch (OperationCanceledException ex)
        {
            CancellationToken token = ex.CancellationToken.IsCancellationRequested
                ? ex.CancellationToken
                : new CancellationToken(canceled: true);
            return Task.FromCanceled(token);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            return Task.FromException(ex);
        }
    }

    private void Update(MachineId machineId, Action<LoopbackMachine> action)
    {
        LoopbackMachine machine = Find(machineId);
        lock (machine.Gate)
        {
            EnsureLive(machine);
            action(machine);
        }
    }

    private LoopbackMachine Find(MachineId machineId)
    {
        ArgumentNullException.ThrowIfNull(machineId);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!machinesById.TryGetValue(machineId.Value, out LoopbackMachine? machine))
            {
                throw Unknown(machineId);
            }

            return machine;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void Notify(Action<IHostNotificationSink> notify)
    {
        IHostNotificationSink[] snapshot;
        lock (tableLock)
        {
            snapshot = sinks.ToArray();
        }

        foreach (IHostNotificationSink sink in snapshot)
        {
            notify(sink);
        }
    }

    private void Unsubscribe(IHostNotificationSink sink)
    {
        lock (tableLock)
        {
            sinks.Remove(sink);
        }
    }

    private sealed class SinkSubscription : IDisposable
    {
        private readonly LoopbackHostSession session;
        private readonly IHostNotificationSink sink;
        private bool disposed;

        internal SinkSubscription(LoopbackHostSession session, IHostNotificationSink sink)
        {
            this.session = session;
            this.sink = sink;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            session.Unsubscribe(sink);
        }
    }
}