// <copyright file="LibraryDiagnosticRoutingTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.UI.Tests;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using BadMango.Emulator.Debug.UI.Editor.Services;
using BadMango.Emulator.Debug.UI.Services;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Verifies library code reports diagnostics through the injected logger
/// instead of writing them to the console.
/// </summary>
[TestFixture]
public class LibraryDiagnosticRoutingTests
{
    /// <summary>
    /// Glyph-editor errors are user-facing diagnostics, not console debug prints.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaTest]
    public async Task ShowErrorDialogAsync_DoesNotWriteTheMessageToTheConsole()
    {
        var window = new Window();
        var service = new GlyphFileService(window);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            await service.ShowErrorDialogAsync("disk slipped");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Multiple(() =>
        {
            Assert.That(stdout.ToString(), Does.Not.Contain("disk slipped"));
            Assert.That(stderr.ToString(), Does.Not.Contain("disk slipped"));
        });
    }

    /// <summary>
    /// A supplied logger receives the glyph-editor error, and the console stays quiet.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaTest]
    public async Task ShowErrorDialogAsync_LogsTheMessageAndDoesNotWriteTheConsole()
    {
        var sink = new TestLogEventSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var window = new Window();
        var service = new GlyphFileService(window, logger);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            await service.ShowErrorDialogAsync("disk slipped");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Multiple(() =>
        {
            Assert.That(stdout.ToString(), Does.Not.Contain("disk slipped"));
            Assert.That(stderr.ToString(), Does.Not.Contain("disk slipped"));
            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Error));
            Assert.That(sink.Events[0].RenderMessage(), Does.Contain("disk slipped"));
        });
    }

    /// <summary>
    /// Avalonia startup failures are logged and do not write the process console.
    /// </summary>
    [Test]
    public void ReportInitializationFailure_LogsTheExceptionAndDoesNotWriteTheConsole()
    {
        var sink = new TestLogEventSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var started = new ManualResetEventSlim(false);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            AvaloniaBootstrapper.ReportInitializationFailure(
                logger,
                new InvalidOperationException("display missing"),
                started);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Multiple(() =>
        {
            Assert.That(started.IsSet, Is.True);
            Assert.That(stdout.ToString(), Does.Not.Contain("display missing"));
            Assert.That(stderr.ToString(), Does.Not.Contain("display missing"));
            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Error));
            Assert.That(sink.Events[0].Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(sink.Events[0].RenderMessage(), Does.Contain("display missing"));
        });
    }

    private sealed class TestLogEventSink : ILogEventSink
    {
        private readonly List<LogEvent> events = [];
        private readonly Lock eventsLock = new();

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (eventsLock)
                {
                    return events.ToList();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (eventsLock)
            {
                events.Add(logEvent);
            }
        }
    }
}