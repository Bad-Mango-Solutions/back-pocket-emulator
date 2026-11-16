// <copyright file="AvaloniaBootstrapper.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.UI.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using Serilog;

/// <summary>
/// Bootstraps and manages the Avalonia UI thread for the console debugger.
/// </summary>
/// <remarks>
/// <para>
/// This class allows the console REPL to coexist with Avalonia by running
/// the Avalonia UI thread in the background. The REPL continues to run on
/// the main thread while Avalonia handles UI operations on its dedicated thread.
/// </para>
/// <para>
/// The bootstrapper follows the threading model described in the Debug Video
/// Window specification: a dedicated Avalonia UI thread owns all windows and
/// rendering, while the emulator and console run on separate threads.
/// </para>
/// </remarks>
/// <seealso href="https://github.com/Bad-Mango-Solutions/back-pocket-emulator/blob/main/specs/video/Pocket2e%20Debug%20Video%20Window%20(Avalonia)%20%E2%80%94%20Specification.md#8-threading-model">
/// Threading Model Specification
/// </seealso>
public sealed class AvaloniaBootstrapper : IDisposable
{
    /// <summary>
    /// Timeout for waiting for Avalonia to fully initialize.
    /// </summary>
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for waiting for the Avalonia thread to complete during shutdown.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private static readonly Lock SyncLock = new();
    private static AvaloniaBootstrapper? instance;
    private static bool isInitialized;

    private readonly ILogger logger;
    private readonly Thread avaloniaThread;
    private readonly ManualResetEventSlim startedEvent = new(false);
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Action? startupOverride;
    private Exception? startupFailure;
    private bool disposed;

    private AvaloniaBootstrapper(ILogger? logger, Action? startupOverride)
    {
        this.logger = logger ?? Serilog.Core.Logger.None;
        this.startupOverride = startupOverride;
        this.avaloniaThread = new Thread(this.RunAvalonia)
        {
            Name = "Avalonia UI Thread",
            IsBackground = true,
        };
    }

    /// <summary>
    /// Gets a value indicating whether Avalonia has been initialized and is running.
    /// </summary>
    public static bool IsRunning => isInitialized && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Ensures Avalonia is initialized and running on a background thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is thread-safe and can be called multiple times. Subsequent
    /// calls after a successful start return immediately. A failed start leaves
    /// the bootstrapper uninitialized so a later call can retry.
    /// </para>
    /// <para>
    /// The method blocks until Avalonia is fully initialized and ready to
    /// accept UI operations, or until startup fails and the caller is unblocked.
    /// </para>
    /// </remarks>
    public static void EnsureInitialized()
    {
        EnsureInitialized(logger: null);
    }

    /// <summary>
    /// Ensures Avalonia is initialized and running on a background thread.
    /// </summary>
    /// <param name="logger">
    /// Receives startup failures. When <see langword="null"/>, failures are not written to the console.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method is thread-safe and can be called multiple times. Subsequent
    /// calls after a successful start return immediately. A failed start leaves
    /// the bootstrapper uninitialized, so a later call can retry and supply the
    /// logger that records that attempt.
    /// </para>
    /// <para>
    /// The method blocks until Avalonia is fully initialized and ready to
    /// accept UI operations, or until startup fails and the caller is unblocked.
    /// </para>
    /// </remarks>
    public static void EnsureInitialized(ILogger? logger)
    {
        if (isInitialized)
        {
            return;
        }

        TryInitialize(logger, startupOverride: null);
    }

    /// <summary>
    /// Shuts down Avalonia gracefully.
    /// </summary>
    public static void Shutdown()
    {
        lock (SyncLock)
        {
            if (instance is not null)
            {
                instance.Dispose();
                instance = null;
                isInitialized = false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        // Signal shutdown
        this.cancellationTokenSource.Cancel();

        // Request Avalonia to shutdown from the UI thread
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            Dispatcher.UIThread.InvokeAsync(() => lifetime.Shutdown());
        }

        // Wait for the thread to complete. Join throws if Start never ran.
        if ((this.avaloniaThread.ThreadState & ThreadState.Unstarted) == 0)
        {
            this.avaloniaThread.Join(ShutdownTimeout);
        }

        this.startedEvent.Dispose();
        this.cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Starts Avalonia, or <paramref name="startupOverride"/> when a test supplies one.
    /// </summary>
    /// <param name="logger">Receives startup failures.</param>
    /// <param name="startupOverride">
    /// Replacement for the Avalonia startup loop. A thrown exception is a failed start.
    /// </param>
    /// <returns><see langword="true"/> when Avalonia is left initialized.</returns>
    internal static bool TryInitialize(ILogger? logger, Action? startupOverride)
    {
        lock (SyncLock)
        {
            if (isInitialized)
            {
                return true;
            }

            var bootstrapper = new AvaloniaBootstrapper(logger, startupOverride);
            bootstrapper.Start();
            if (bootstrapper.startupFailure is not null)
            {
                bootstrapper.Dispose();
                return false;
            }

            instance = bootstrapper;
            isInitialized = true;
            return true;
        }
    }

    /// <summary>
    /// Disposes any bootstrapper created by tests and clears the initialized flag.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (SyncLock)
        {
            if (instance is not null)
            {
                instance.Dispose();
                instance = null;
            }

            isInitialized = false;
        }
    }

    /// <summary>
    /// Records an Avalonia startup failure and unblocks <see cref="Start"/>.
    /// </summary>
    /// <param name="logger">The logger that receives the failure.</param>
    /// <param name="exception">The exception thrown while starting Avalonia.</param>
    /// <param name="startedEvent">The event that unblocks the caller waiting for startup.</param>
    internal static void ReportInitializationFailure(ILogger logger, Exception exception, ManualResetEventSlim startedEvent)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(startedEvent);

        logger.Error(exception, "Avalonia initialization failed: {Message}", exception.Message);
        startedEvent.Set();
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<DebugApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private void Start()
    {
        this.avaloniaThread.Start();

        // Wait for Avalonia to be fully initialized before returning
        if (!this.startedEvent.Wait(InitializationTimeout))
        {
            throw new TimeoutException("Avalonia failed to initialize within the timeout period.");
        }
    }

    private void RunAvalonia()
    {
        try
        {
            if (this.startupOverride is not null)
            {
                this.startupOverride();
                this.startedEvent.Set();
                return;
            }

            BuildAvaloniaApp()
                .AfterSetup(_ =>
                {
                    // Signal that Avalonia is ready
                    this.startedEvent.Set();
                })
                .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            this.startupFailure = ex;
            ReportInitializationFailure(this.logger, ex, this.startedEvent);
        }
    }
}