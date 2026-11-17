// <copyright file="InspectionMcpClientTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using System.IO.Pipelines;
using System.Text.Json;

using BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Serilog;

/// <summary>
/// Drives the inspection, breakpoint, and watchpoint tools through an in-memory MCP client.
/// </summary>
[NonParallelizable]
public sealed class InspectionMcpClientTests
{
    private const int RomSize = 0x4000;
    private const uint ResetAddress = 0xC000;

    /// <summary>
    /// Reads registers, memory, and disassembly, and writes a short program into RAM.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task RegistersMemoryAndDisassembly_RoundTripOnGeneratedRom()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-inspect-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-inspect.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-inspect" });
            await session.CallAsync("reset");

            JsonElement registers = await session.CallAsync("get_registers");
            Assert.Multiple(() =>
            {
                Assert.That(registers.GetProperty("cpuType").GetString(), Is.EqualTo("65C02"));
                Assert.That(registers.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress));
            });

            JsonElement memory = await session.CallAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "$C000",
                ["length"] = 3,
            });
            Assert.Multiple(() =>
            {
                Assert.That(memory.GetProperty("address").GetUInt32(), Is.EqualTo(ResetAddress));
                Assert.That(memory.GetProperty("length").GetInt32(), Is.EqualTo(3));
                Assert.That(memory.GetProperty("data").GetString(), Is.EqualTo("EAEADB"));
                Assert.That(memory.GetProperty("reportedFaults").GetArrayLength(), Is.EqualTo(0));
            });

            JsonElement defaults = await session.CallAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "0xC000",
            });
            Assert.That(defaults.GetProperty("length").GetInt32(), Is.EqualTo(DebugMcpLimits.DefaultReadLength));

            JsonElement written = await session.CallAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0300",
                ["data"] = "A9 00 60",
            });
            Assert.Multiple(() =>
            {
                Assert.That(written.GetProperty("address").GetUInt32(), Is.EqualTo(0x0300));
                Assert.That(written.GetProperty("length").GetInt32(), Is.EqualTo(3));
            });

            JsonElement poked = await session.CallAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "768",
                ["length"] = 3,
            });
            Assert.That(poked.GetProperty("data").GetString(), Is.EqualTo("A90060"));

            JsonElement listing = await session.CallAsync("disassemble", new Dictionary<string, object?>
            {
                ["address"] = "$0300",
                ["count"] = 2,
            });
            Assert.That(listing.GetArrayLength(), Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(listing[0].GetProperty("address").GetUInt32(), Is.EqualTo(0x0300));
                Assert.That(listing[0].GetProperty("mnemonic").GetString(), Is.EqualTo("LDA"));
                Assert.That(listing[0].GetProperty("operand").GetString(), Is.EqualTo("#$00"));
                Assert.That(listing[0].GetProperty("bytes").GetString(), Is.EqualTo("A9 00"));
                Assert.That(listing[1].GetProperty("mnemonic").GetString(), Is.EqualTo("RTS"));
            });

            JsonElement atPc = await session.CallAsync("disassemble", new Dictionary<string, object?>
            {
                ["count"] = 1,
            });
            Assert.Multiple(() =>
            {
                Assert.That(atPc[0].GetProperty("address").GetUInt32(), Is.EqualTo(ResetAddress));
                Assert.That(atPc[0].GetProperty("mnemonic").GetString(), Is.EqualTo("NOP"));
            });
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// A breakpoint stops <c>run_until</c> before the instruction at that address executes.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task RunUntil_HitsBreakpoint()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-bp-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-bp.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-bp" });
            await session.CallAsync("reset");

            JsonElement breakpoint = await session.CallAsync("set_breakpoint", new Dictionary<string, object?>
            {
                ["address"] = "$C001",
                ["label"] = "second-nop",
            });
            Assert.Multiple(() =>
            {
                Assert.That(breakpoint.GetProperty("address").GetUInt32(), Is.EqualTo(ResetAddress + 1));
                Assert.That(breakpoint.GetProperty("label").GetString(), Is.EqualTo("second-nop"));
                Assert.That(breakpoint.GetProperty("enabled").GetBoolean(), Is.True);
            });

            JsonElement run = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["condition"] = "bp",
                ["maxCycles"] = 10_000,
            });
            Assert.Multiple(() =>
            {
                Assert.That(run.GetProperty("stopReason").GetString(), Is.EqualTo("Breakpoint"));
                Assert.That(run.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress + 1));
            });
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// A watchpoint stops <c>run_until</c> when the instruction accesses the watched address.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task RunUntil_HitsWatchpoint()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-watch-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-watch.json", 0xAD, 0x00, 0x02, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-watch" });
            await session.CallAsync("reset");
            await session.CallAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0200",
                ["data"] = "42",
            });

            JsonElement watch = await session.CallAsync("set_watchpoint", new Dictionary<string, object?>
            {
                ["address"] = "$0200",
                ["access"] = "r",
                ["stopOnHit"] = true,
                ["label"] = "scratch",
            });
            Assert.Multiple(() =>
            {
                Assert.That(watch.GetProperty("address").GetUInt32(), Is.EqualTo(0x0200));
                Assert.That(watch.GetProperty("access").GetString(), Is.EqualTo("Read"));
                Assert.That(watch.GetProperty("stopOnHit").GetBoolean(), Is.True);
                Assert.That(watch.GetProperty("label").GetString(), Is.EqualTo("scratch"));
            });

            JsonElement run = await session.CallAsync("run_until", new Dictionary<string, object?>
            {
                ["condition"] = "watch",
                ["maxCycles"] = 10_000,
            });
            Assert.Multiple(() =>
            {
                Assert.That(run.GetProperty("stopReason").GetString(), Is.EqualTo("Watchpoint"));
                Assert.That(run.GetProperty("programCounter").GetUInt32(), Is.EqualTo(ResetAddress + 3));
            });
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// Invalid arguments come back as emudbg error envelopes.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task InvalidArguments_ReturnEnvelopeErrors()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-args-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-args.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-args" });

            JsonElement badAddress = await session.CallErrorAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "nope",
                ["length"] = 1,
            });
            Assert.That(badAddress.GetProperty("message").GetString(), Does.Contain("Invalid address"));

            JsonElement zeroLength = await session.CallErrorAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0000",
                ["length"] = 0,
            });
            Assert.That(zeroLength.GetProperty("message").GetString(), Does.Contain("1 through"));

            JsonElement tooLong = await session.CallErrorAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0000",
                ["length"] = DebugMcpLimits.MaxReadLength + 1,
            });
            Assert.That(tooLong.GetProperty("message").GetString(), Does.Contain(DebugMcpLimits.MaxReadLength.ToString()));

            JsonElement badData = await session.CallErrorAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0300",
                ["data"] = "GG",
            });
            Assert.That(badData.GetProperty("message").GetString(), Does.Contain("hex"));

            JsonElement emptyWrite = await session.CallErrorAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0300",
                ["data"] = " ",
            });
            Assert.That(emptyWrite.GetProperty("message").GetString(), Does.Contain("byte"));

            string oversized = new('A', (DebugMcpLimits.MaxWriteLength + 1) * 2);
            JsonElement longWrite = await session.CallErrorAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0300",
                ["data"] = oversized,
            });
            Assert.That(longWrite.GetProperty("message").GetString(), Does.Contain(DebugMcpLimits.MaxWriteLength.ToString()));

            JsonElement zeroCount = await session.CallErrorAsync("disassemble", new Dictionary<string, object?>
            {
                ["count"] = 0,
            });
            Assert.That(zeroCount.GetProperty("message").GetString(), Does.Contain("Disassembly count"));

            JsonElement hugeCount = await session.CallErrorAsync("disassemble", new Dictionary<string, object?>
            {
                ["address"] = "$C000",
                ["count"] = DebugMcpLimits.MaxDisassemblyCount + 1,
            });
            Assert.That(hugeCount.GetProperty("message").GetString(), Does.Contain(DebugMcpLimits.MaxDisassemblyCount.ToString()));

            await session.CallAsync("set_breakpoint", new Dictionary<string, object?> { ["address"] = "$C001" });
            JsonElement duplicate = await session.CallErrorAsync("set_breakpoint", new Dictionary<string, object?>
            {
                ["address"] = "$C001",
            });
            Assert.That(duplicate.GetProperty("message").GetString(), Does.Contain("already exists"));

            JsonElement badAccess = await session.CallErrorAsync("set_watchpoint", new Dictionary<string, object?>
            {
                ["address"] = "$0200",
                ["access"] = "execute",
            });
            Assert.That(badAccess.GetProperty("message").GetString(), Does.Contain("ReadWrite"));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// Reads and writes past the mapped address space report <c>Unmapped</c> through the shared formatter.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task MemoryFaults_UseSharedFaultKindFormatting()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-fault-").FullName;
        try
        {
            WriteRom(sandbox, "mcp-fault.json", 0xEA, 0xEA, 0xDB);
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-fault" });

            JsonElement read = await session.CallAsync("read_memory", new Dictionary<string, object?>
            {
                ["address"] = "$FFFE",
                ["length"] = 4,
            });
            JsonElement reports = read.GetProperty("reportedFaults");
            Assert.Multiple(() =>
            {
                Assert.That(read.GetProperty("faults").GetString(), Is.EqualTo("00000101"));
                Assert.That(reports.GetArrayLength(), Is.EqualTo(2));
                Assert.That(reports[0].GetProperty("address").GetUInt32(), Is.EqualTo(0x10000));
                Assert.That(reports[0].GetProperty("kind").GetString(), Is.EqualTo("Unmapped"));
                Assert.That(reports[1].GetProperty("address").GetUInt32(), Is.EqualTo(0x10001));
                Assert.That(reports[1].GetProperty("kind").GetString(), Is.EqualTo("Unmapped"));
            });

            JsonElement write = await session.CallErrorAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$10000",
                ["data"] = "FF",
            });
            Assert.Multiple(() =>
            {
                Assert.That(write.GetProperty("message").GetString(), Does.Contain("Unmapped"));
                Assert.That(write.GetProperty("faults")[0].GetProperty("address").GetUInt32(), Is.EqualTo(0x10000));
                Assert.That(write.GetProperty("faults")[0].GetProperty("kind").GetString(), Is.EqualTo("Unmapped"));
            });
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static void WriteRom(string directory, string profileFileName, params byte[] program)
    {
        byte[] rom = new byte[RomSize];
        program.CopyTo(rom, 0);
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

        public Task<JsonElement> CallAsync(string name, Dictionary<string, object?>? arguments = null)
        {
            return this.CallCoreAsync(name, arguments, expectError: false);
        }

        public Task<JsonElement> CallErrorAsync(string name, Dictionary<string, object?>? arguments = null)
        {
            return this.CallCoreAsync(name, arguments, expectError: true);
        }

        public async ValueTask DisposeAsync()
        {
            await this.Client.DisposeAsync();
            await this.host.StopAsync();
            this.host.Dispose();
            Restore(this.previousLogger);
        }

        private static JsonElement ReadEnvelope(CallToolResult result)
        {
            if (result.StructuredContent is JsonElement structured && structured.ValueKind == JsonValueKind.Object)
            {
                return structured;
            }

            string text = string.Join(Environment.NewLine, result.Content);
            return JsonDocument.Parse(text).RootElement.Clone();
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

        private async Task<JsonElement> CallCoreAsync(string name, Dictionary<string, object?>? arguments, bool expectError)
        {
            CallToolResult result = await this.Client.CallToolAsync(name, arguments);
            if (expectError)
            {
                Assert.That(result.IsError, Is.True, Describe(result));
            }
            else
            {
                Assert.That(result.IsError, Is.Not.True, Describe(result));
            }

            JsonElement root = ReadEnvelope(result);
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"), Describe(result));
            Assert.That(root.TryGetProperty("payload", out JsonElement payload), Is.True, Describe(result));
            Assert.That(root.TryGetProperty("timestamp", out _), Is.True, Describe(result));
            return payload;
        }
    }
}