// <copyright file="StdoutSilenceTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Proves process startup leaves stdout for the MCP protocol.
/// </summary>
[NonParallelizable]
public sealed class StdoutSilenceTests
{
    /// <summary>
    /// The host process writes its startup log to stderr and nothing to stdout.
    /// </summary>
    /// <returns>A task that completes when the process has exited.</returns>
    [Test]
    public async Task Startup_WritesNothingNonProtocolToStdout()
    {
        string configuration = typeof(StdoutSilenceTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        string repo = FindRepoRoot();
        string[] segments = [repo, "src", "BadMango.Emulator.Debug.Mcp", "bin", configuration, "net10.0"];
        string output = Path.Join(segments);
        string dll = Path.Join(output, "emudbg-mcp.dll");
        Assert.That(File.Exists(dll), Is.True, $"Build the host before this test. Missing {dll}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = output,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add(dll);
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            stderr.AppendLine(args.Data);
            if (args.Data.Contains("Debug MCP host starting", StringComparison.Ordinal))
            {
                started.TrySetResult(true);
            }
        };

        Assert.That(process.Start(), Is.True);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await started.Task.WaitAsync(timeout.Token);
            process.StandardInput.Close();
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(exitTimeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(process.ExitCode, Is.EqualTo(0), stderr.ToString());
            Assert.That(stdout.ToString(), Is.Empty, stderr.ToString());
        });
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? directory = new FileInfo(sourceFile).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "BackPocketEmulator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test source path.");
    }
}