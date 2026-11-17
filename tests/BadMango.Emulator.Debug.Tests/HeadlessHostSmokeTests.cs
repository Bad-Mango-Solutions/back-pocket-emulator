// <copyright file="HeadlessHostSmokeTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Tests;

using System.Diagnostics;

/// <summary>
/// Starts the headless debug host on a machine with no display server.
/// </summary>
[TestFixture]
public sealed class HeadlessHostSmokeTests
{
    /// <summary>
    /// <c>emudbg --headless</c> runs a command, exits, and does not load Avalonia or the debug UI.
    /// </summary>
    [Test]
    public void HeadlessHost_RunsCommandWithoutLoadingAvalonia()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "emudbg.dll");
        Assert.That(File.Exists(dll), Is.True, $"Headless host was not copied to {dll}.");

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = AppContext.BaseDirectory;
        process.StartInfo.ArgumentList.Add(dll);
        process.StartInfo.ArgumentList.Add(DebugHostArguments.HeadlessOption);
        process.StartInfo.ArgumentList.Add(DebugHostArguments.DumpAssembliesOption);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.Environment.Remove("DISPLAY");
        process.StartInfo.Environment.Remove("WAYLAND_DISPLAY");

        Assert.That(process.Start(), Is.True);
        process.StandardInput.WriteLine("version");
        process.StandardInput.Close();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(60_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        List<string> names = LoadedAssemblyNames(stdout);
        Assert.Multiple(() =>
        {
            Assert.That(exited, Is.True, "emudbg --headless did not exit. stderr: " + stderr);
            Assert.That(process.ExitCode, Is.EqualTo(0), stderr + stdout);
            Assert.That(stdout, Does.Contain("Emulator Debug Console"));
            Assert.That(names, Does.Contain("emudbg"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Debug.UI"));
            Assert.That(names.Any(static name => name.StartsWith("Avalonia", StringComparison.Ordinal)), Is.False, stdout);
        });
    }

    /// <summary>
    /// A headless <c>--exec</c> batch exits 0 and does not load Avalonia.
    /// </summary>
    [Test]
    public void HeadlessExec_Version_ExitsZeroWithoutAvalonia()
    {
        HostRun run = RunHost(["--headless", "--no-banner", "--exec", "version", "--dump-assemblies"]);
        List<string> names = LoadedAssemblyNames(run.Stdout);
        Assert.Multiple(() =>
        {
            Assert.That(run.Exited, Is.True, run.Stderr);
            Assert.That(run.ExitCode, Is.EqualTo(0), run.Stderr + run.Stdout);
            Assert.That(run.Stdout, Does.Contain("Emulator Debug Console"));
            Assert.That(names, Does.Not.Contain("BadMango.Emulator.Debug.UI"));
            Assert.That(names.Any(static name => name.StartsWith("Avalonia", StringComparison.Ordinal)), Is.False, run.Stdout);
        });
    }

    /// <summary>
    /// An unknown command in a batch exits 1.
    /// </summary>
    [Test]
    public void HeadlessExec_UnknownCommand_ExitsOne()
    {
        HostRun run = RunHost(["--headless", "--no-banner", "--exec", "not-a-command"]);
        Assert.Multiple(() =>
        {
            Assert.That(run.Exited, Is.True, run.Stderr);
            Assert.That(run.ExitCode, Is.EqualTo(1), run.Stdout);
            Assert.That(run.Stderr, Does.Contain("Unknown command"));
        });
    }

    /// <summary>
    /// A missing script file is reported on stderr and exits 1.
    /// </summary>
    [Test]
    public void HeadlessFile_MissingScript_ExitsOneOnStderr()
    {
        HostRun run = RunHost(["--headless", "--file", "missing-emudbg-script.txt"]);
        Assert.Multiple(() =>
        {
            Assert.That(run.Exited, Is.True);
            Assert.That(run.ExitCode, Is.EqualTo(1));
            Assert.That(run.Stderr, Does.Contain("Script file not found"));
        });
    }

    /// <summary>
    /// Invalid startup options exit 2.
    /// </summary>
    [Test]
    public void HeadlessOptions_InvalidMaxCycles_ExitsTwo()
    {
        HostRun run = RunHost(["--max-cycles"]);
        Assert.Multiple(() =>
        {
            Assert.That(run.Exited, Is.True);
            Assert.That(run.ExitCode, Is.EqualTo(2));
            Assert.That(run.Stderr, Does.Contain("missing value"));
        });
    }

    private static HostRun RunHost(IReadOnlyList<string> arguments)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "emudbg.dll");
        Assert.That(File.Exists(dll), Is.True, $"Headless host was not copied to {dll}.");

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = AppContext.BaseDirectory;
        process.StartInfo.ArgumentList.Add(dll);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.Environment.Remove("DISPLAY");
        process.StartInfo.Environment.Remove("WAYLAND_DISPLAY");

        Assert.That(process.Start(), Is.True);
        process.StandardInput.Close();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(60_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new HostRun(exited, process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static List<string> LoadedAssemblyNames(string stdout)
    {
        var names = new List<string>();
        foreach (string line in stdout.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(DebugHostArguments.LoadedAssemblyPrefix, StringComparison.Ordinal))
            {
                names.Add(trimmed[DebugHostArguments.LoadedAssemblyPrefix.Length..]);
            }
        }

        return names;
    }

    private readonly record struct HostRun(bool Exited, int ExitCode, string Stdout, string Stderr);
}