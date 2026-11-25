// <copyright file="LifecycleMcpClientTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;

using BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Serilog;

/// <summary>
/// Drives the lifecycle tools through an in-memory MCP client.
/// </summary>
[NonParallelizable]
public sealed class LifecycleMcpClientTests
{
    private const int RomSize = 0x4000;
    private const uint ResetAddress = 0xC000;

    /// <summary>
    /// Lists the lifecycle tools, then reset, step, and run-until on a ROM built in the test.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task Client_ListsTools_ResetsSteps_AndRunsUntilOnGeneratedRom()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-rom-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-test-rom.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);

            IList<McpClientTool> tools = await session.Client.ListToolsAsync();
            string[] names = tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.That(names, Is.EqualTo(new[]
            {
                "disassemble",
                "disk_list_files",
                "disk_read_file",
                "get_boot_log",
                "get_file_activity",
                "get_guest_os",
                "get_registers",
                "get_screen_text",
                "load_profile",
                "mount_disk",
                "pause",
                "read_memory",
                "reset",
                "run_until",
                "set_breakpoint",
                "set_watchpoint",
                "step",
                "type_keys",
                "write_memory",
            }));
            Assert.That(tools.All(static tool => !string.IsNullOrWhiteSpace(tool.Description)), Is.True);

            JsonElement loaded = await session.CallAsync("load_profile", new Dictionary<string, object?>
            {
                ["name"] = "mcp-test-rom",
            });
            Assert.That(loaded.GetProperty("name").GetString(), Is.EqualTo("mcp-test-rom"));

            JsonElement reset = await session.CallAsync("reset", new Dictionary<string, object?>
            {
                ["kind"] = "Warm",
            });
            Assert.Multiple(() =>
            {
                Assert.That(reset.GetProperty("kind").GetString(), Is.EqualTo("Warm"));
                Assert.That(reset.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress));
            });

            JsonElement step = await session.CallAsync("step", new Dictionary<string, object?>
            {
                ["count"] = 1,
            });
            Assert.Multiple(() =>
            {
                Assert.That(step.GetProperty("instructionsExecuted").GetUInt64(), Is.EqualTo(1UL));
                Assert.That(step.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress + 1));
            });

            JsonElement budgeted = await session.CallAsync("step", new Dictionary<string, object?>
            {
                ["count"] = 10,
                ["maxCycles"] = 1,
            });
            Assert.That(budgeted.GetProperty("instructionsExecuted").GetUInt64(), Is.EqualTo(1UL));

            await session.CallAsync("reset");
            JsonElement run = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["condition"] = "$C002",
                ["maxCycles"] = 10_000,
            });
            Assert.Multiple(() =>
            {
                Assert.That(run.GetProperty("stopReason").GetString(), Is.EqualTo("Breakpoint"));
                Assert.That(run.GetProperty("instructionCount").GetUInt64(), Is.EqualTo(2UL));
                Assert.That(run.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress + 2));
            });

            JsonElement paused = await session.CallAsync("pause");
            Assert.That(paused.TryGetProperty("programCounter", out _), Is.True);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// A budget above the synchronous ceiling returns a run id that poll and stop can use.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task RunUntil_AboveSynchronousCeiling_UsesStartPollStop()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-run-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-loop.json", 0x4C, 0x00, 0xC0);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?>
            {
                ["name"] = "mcp-loop",
            });
            await session.CallAsync("reset");

            JsonElement started = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["condition"] = "cycles",
                ["untilCycles"] = 50_000_000,
                ["maxCycles"] = DebugMcpBudgets.SynchronousMaxCycles + 1,
                ["action"] = "run",
            });
            string? runId = started.GetProperty("runId").GetString();
            Assert.Multiple(() =>
            {
                Assert.That(started.GetProperty("running").GetBoolean(), Is.True);
                Assert.That(runId, Is.Not.Null.And.Not.Empty);
            });

            JsonElement polled = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["action"] = "poll",
                ["runId"] = runId,
            });
            Assert.That(polled.GetProperty("runId").GetString(), Is.EqualTo(runId));

            JsonElement stopped = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["action"] = "stop",
                ["runId"] = runId,
            });
            Assert.Multiple(() =>
            {
                Assert.That(stopped.GetProperty("running").GetBoolean(), Is.False);
                Assert.That(stopped.GetProperty("action").GetString(), Is.EqualTo("stop"));
            });
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// <c>reset</c> with <c>log</c> installs the boot monitor for a cold reset, and rejects a warm reset.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task Reset_LogOnColdReset_InstallsTheBootMonitor_AndWarmLogIsAnError()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-log-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-log.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?>
            {
                ["name"] = "mcp-log",
            });

            CallToolResult warm = await session.Client.CallToolAsync(
                "reset",
                new Dictionary<string, object?>
                {
                    ["kind"] = "Warm",
                    ["log"] = true,
                });
            Assert.That(warm.IsError, Is.True);
            Assert.That(string.Join('\n', warm.Content), Does.Contain("cold").IgnoreCase);

            JsonElement before = await session.CallAsync("get_guest_os");
            Assert.That(before.GetProperty("reason").GetString(), Is.EqualTo("Guest OS detection is off."));

            await session.CallAsync("reset", new Dictionary<string, object?>
            {
                ["kind"] = "Hard",
                ["log"] = true,
            });
            JsonElement afterHard = await session.CallAsync("get_guest_os");
            Assert.That(afterHard.GetProperty("reason").GetString(), Is.Not.EqualTo("Guest OS detection is off."));

            await session.CallAsync("reset", new Dictionary<string, object?>
            {
                ["kind"] = "PowerOn",
                ["log"] = true,
            });
            JsonElement afterPowerOn = await session.CallAsync("get_guest_os");
            Assert.That(afterPowerOn.GetProperty("reason").GetString(), Is.Not.EqualTo("Guest OS detection is off."));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// An absolute profile path outside the sandbox is rejected.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task LoadProfile_OutsideSandbox_IsAnError()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-deny-").FullName;
        string outside = Directory.CreateTempSubdirectory("mcp-outside-").FullName;
        try
        {
            string foreignProfile = Path.Join(outside, "foreign.json");
            File.WriteAllText(foreignProfile, "{}");
            await using McpSession session = await McpSession.StartAsync(sandbox);
            CallToolResult result = await session.Client.CallToolAsync(
                "load_profile",
                new Dictionary<string, object?>
                {
                    ["name"] = foreignProfile,
                });

            Assert.That(result.IsError, Is.True);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static void WriteRom(string directory, string profileFileName, byte first, byte second, byte third)
    {
        byte[] rom = new byte[RomSize];
        rom[0] = first;
        rom[1] = second;
        rom[2] = third;
        rom[0x3FFC] = 0x00;
        rom[0x3FFD] = 0xC0;
        if (Path.IsPathRooted(profileFileName))
        {
            throw new ArgumentException($"Profile file name must be relative: '{profileFileName}'.", nameof(profileFileName));
        }

        File.WriteAllBytes(Path.Join(directory, "monitor.bin"), rom);
        string profilePath = Path.Join(directory, profileFileName);
        File.WriteAllText(
            profilePath,
            """
            {
              "name": "PROFILE_NAME",
              "displayName": "MCP test ROM",
              "description": "ROM generated by the MCP host test",
              "cpu": { "type": "65C02", "clockSpeed": 1000000 },
              "addressSpace": 16,
              "memory": {
                "rom-images": [
                  { "name": "monitor", "source": "monitor.bin", "size": "0x4000", "required": true }
                ],
                "physical": [
                  { "name": "main-ram-48k", "size": "0xC000", "fill": "0x00" },
                  {
                    "name": "system-rom-16k",
                    "size": "0x4000",
                    "sources": [
                      { "type": "rom-image", "name": "monitor", "rom-image": "monitor", "offset": "0x0000" }
                    ]
                  }
                ],
                "regions": [
                  {
                    "name": "main-ram",
                    "type": "ram",
                    "start": "0x0000",
                    "size": "0xC000",
                    "permissions": "rwx",
                    "source": "main-ram-48k",
                    "source-offset": "0x0000"
                  },
                  {
                    "name": "system-rom",
                    "type": "rom",
                    "start": "0xC000",
                    "size": "0x4000",
                    "permissions": "rx",
                    "source": "system-rom-16k",
                    "source-offset": "0x0000"
                  }
                ]
              }
            }
            """.Replace("PROFILE_NAME", Path.GetFileNameWithoutExtension(profileFileName), StringComparison.Ordinal));
    }

    private sealed class McpSession : IAsyncDisposable
    {
        private readonly ILogger previousLogger;
        private readonly IHost host;

        private McpSession(ILogger previousLogger, IHost host, McpClient client)
        {
            this.previousLogger = previousLogger;
            this.host = host;
            this.Client = client;
        }

        public McpClient Client { get; }

        public static async Task<McpSession> StartAsync(string sandbox)
        {
            Pipe clientToServer = new();
            Pipe serverToClient = new();
            ILogger previous = Log.Logger;
            IHost? host = null;
            try
            {
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    Args = [],
                    ContentRootPath = sandbox,
                });
                DebugMcpHost.Configure(builder);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{DebugMcpOptions.SectionName}:SandboxRoots:0"] = sandbox,
                });
                builder.Services
                    .AddMcpServer()
                    .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
                    .WithToolsFromAssembly(typeof(DebugMcpHost).Assembly, DebugMcpJson.SerializerOptions);

                host = builder.Build();
                await host.StartAsync();
                McpClient client = await McpClient.CreateAsync(new StreamClientTransport(
                    clientToServer.Writer.AsStream(),
                    serverToClient.Reader.AsStream()));
                return new McpSession(previous, host, client);
            }
            catch
            {
                host?.Dispose();
                Restore(previous);
                throw;
            }
        }

        public async Task<JsonElement> CallAsync(string name, Dictionary<string, object?>? arguments = null)
        {
            EnsurePowered(this.host, name);
            CallToolResult result = await this.Client.CallToolAsync(name, arguments);
            Assert.That(result.IsError, Is.Not.True, Describe(result));
            Assert.That(result.StructuredContent, Is.Not.Null, Describe(result));
            JsonElement root = result.StructuredContent!.Value;
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"), Describe(result));
            Assert.That(root.TryGetProperty("payload", out JsonElement payload), Is.True, Describe(result));
            Assert.That(root.TryGetProperty("timestamp", out _), Is.True, Describe(result));
            return payload;
        }

        public async ValueTask DisposeAsync()
        {
            await this.Client.DisposeAsync();
            await this.host.StopAsync();
            this.host.Dispose();
            Restore(this.previousLogger);
        }

        private static void EnsurePowered(IHost host, string toolName)
        {
            if (toolName is not ("reset" or "step" or "run_until" or "pause"))
            {
                return;
            }

            Assembly? infra = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "BadMango.Emulator.Debug.Infrastructure");
            Type? contextType = infra?.GetType("BadMango.Emulator.Debug.Infrastructure.Commands.IDebugContext");
            if (contextType is null)
            {
                return;
            }

            object? context = host.Services.GetService(contextType);
            object? machine = context is null ? null : contextType.GetProperty("Machine")?.GetValue(context);
            if (machine is null || machine.GetType().GetProperty("State")?.GetValue(machine)?.ToString() != "Off")
            {
                return;
            }

            MethodInfo? powerOn = machine.GetType().GetMethod("PowerOnAsync", [typeof(CancellationToken)]);
            if (powerOn?.Invoke(machine, [CancellationToken.None]) is Task powerOnTask)
            {
                powerOnTask.GetAwaiter().GetResult();
            }
        }

        private static void Restore(ILogger previous)
        {
            if (Log.Logger is IDisposable disposable && !ReferenceEquals(Log.Logger, previous))
            {
                disposable.Dispose();
            }

            Log.Logger = previous;
        }

        private static string Describe(CallToolResult result)
        {
            if (result.Content.Count == 0)
            {
                return "tool call failed";
            }

            return string.Join(Environment.NewLine, result.Content);
        }
    }
}