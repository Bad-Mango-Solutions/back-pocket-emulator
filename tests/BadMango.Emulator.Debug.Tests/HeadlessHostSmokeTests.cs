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
}