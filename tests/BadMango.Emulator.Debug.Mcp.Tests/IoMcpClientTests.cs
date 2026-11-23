// <copyright file="IoMcpClientTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;

using BadMango.Emulator.Debug.Mcp;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Tests.FileSystems;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Serilog;

/// <summary>
/// Drives the screen, keyboard, and sandboxed disk tools through an in-memory MCP client.
/// </summary>
[NonParallelizable]
public sealed class IoMcpClientTests
{
    private const int RomSize = 0x3000;

    /// <summary>
    /// The text screen reports bytes written to the text page, and typed keys are accepted.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task ScreenAndKeys_ReadTextAndQueueInput()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-io-").FullName;
        try
        {
            WriteProfile(sandbox, "mcp-io.json");
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-io" });
            await session.CallAsync("reset");
            await session.CallAsync("write_memory", new Dictionary<string, object?>
            {
                ["address"] = "$0400",
                ["data"] = "C8C9",
            });

            JsonElement screen = await session.CallAsync("get_screen_text");
            JsonElement typed = await session.CallAsync("type_keys", new Dictionary<string, object?>
            {
                ["text"] = "Hi",
            });

            Assert.Multiple(() =>
            {
                Assert.That(screen.GetProperty("rows").GetInt32(), Is.EqualTo(24));
                Assert.That(screen.GetProperty("columns").GetInt32(), Is.EqualTo(40));
                Assert.That(screen.GetProperty("isTextMode").GetBoolean(), Is.True);
                Assert.That(screen.GetProperty("lines")[0].GetString(), Does.StartWith("HI"));
                Assert.That(typed.GetProperty("accepted").GetInt32(), Is.EqualTo(2));
                Assert.That(typed.GetProperty("rejected").GetBoolean(), Is.False);
            });
        }
        finally
        {
            DeleteDirectory(sandbox);
        }
    }

    /// <summary>
    /// A DOS image can be mounted and read, and the same file can be read from the image path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task MountListAndRead_DosImage_RoundTripsWithoutWritingHostFiles()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-disk-").FullName;
        try
        {
            WriteProfile(sandbox, "mcp-disk.json");
            string image = WriteDosVolume(sandbox, "note.do");
            await using McpSession session = await McpSession.StartAsync(sandbox);
            await session.CallAsync("load_profile", new Dictionary<string, object?> { ["name"] = "mcp-disk" });

            string[] before = Directory.GetFiles(sandbox);
            JsonElement mounted = await session.CallAsync("mount_disk", new Dictionary<string, object?>
            {
                ["path"] = image,
                ["slot"] = 6,
                ["drive"] = 1,
                ["writeProtect"] = true,
            });
            JsonElement catalog = await session.CallAsync("disk_list_files", new Dictionary<string, object?>
            {
                ["slot"] = 6,
                ["drive"] = 1,
            });
            JsonElement fromDrive = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["slot"] = 6,
                ["drive"] = 1,
                ["filename"] = "NOTE",
            });
            JsonElement fromImage = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = image,
                ["filename"] = "NOTE",
            });

            Assert.Multiple(() =>
            {
                Assert.That(mounted.GetProperty("slot").GetInt32(), Is.EqualTo(6));
                Assert.That(mounted.GetProperty("drive").GetInt32(), Is.EqualTo(1));
                Assert.That(mounted.GetProperty("writeProtect").GetBoolean(), Is.True);
                Assert.That(mounted.GetProperty("path").GetString(), Is.EqualTo(Path.GetFullPath(image)));
                Assert.That(catalog.GetProperty("kind").GetString(), Is.EqualTo("Dos33"));
                Assert.That(catalog.GetProperty("source").GetString(), Is.EqualTo("drive"));
                Assert.That(catalog.GetProperty("volumeName").GetString(), Is.EqualTo("254"));
                Assert.That(Names(catalog), Does.Contain("NOTE"));
                Assert.That(string.Join('\n', DisplayNames(catalog)), Does.Contain("\\i{"));
                Assert.That(fromDrive.GetProperty("text").GetString(), Does.StartWith("HI\n"));
                Assert.That(Decode(fromDrive)[..3], Is.EqualTo(new byte[] { 0xC8, 0xC9, 0x8D }));
                Assert.That(fromDrive.GetProperty("view").GetString(), Is.EqualTo("logical"));
                Assert.That(fromDrive.GetProperty("length").GetInt32(), Is.EqualTo(Decode(fromDrive).Length));
                Assert.That(fromImage.GetProperty("source").GetString(), Is.EqualTo("image"));
                Assert.That(fromImage.GetProperty("text").GetString(), Is.EqualTo(fromDrive.GetProperty("text").GetString()));
                Assert.That(Directory.GetFiles(sandbox), Is.EquivalentTo(before));
            });
        }
        finally
        {
            DeleteDirectory(sandbox);
        }
    }

    /// <summary>
    /// ProDOS and Pascal images use the same readers as <c>disk cat</c> and <c>disk read</c>.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task ListAndRead_ProdosAndPascalImages()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-fs-").FullName;
        try
        {
            var prodosBuilder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "BLANK");
            prodosBuilder.AddSubdirectory("SUB", [new ProdosFixture.SeedlingSpec("NOTE", ProdosFileType.Text, 0, "Hi"u8.ToArray(), false)]);
            string prodos = WriteBytes(sandbox, "volume.hdv", prodosBuilder.Build().Image);
            var pascalBuilder = new PascalImageBuilder(8, "PASCAL");
            pascalBuilder.AddFile("NOTE.TEXT", PascalFileKind.Text, "Hi"u8.ToArray());
            string pascal = WriteBytes(sandbox, "pascal.hdv", pascalBuilder.Build().Image);

            await using McpSession session = await McpSession.StartAsync(sandbox);
            JsonElement prodosList = await session.CallAsync("disk_list_files", new Dictionary<string, object?>
            {
                ["image"] = prodos,
                ["path"] = "SUB",
            });
            JsonElement prodosFile = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = prodos,
                ["filename"] = "SUB/NOTE",
            });
            JsonElement pascalList = await session.CallAsync("disk_list_files", new Dictionary<string, object?>
            {
                ["image"] = pascal,
            });
            JsonElement pascalFile = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = pascal,
                ["filename"] = "NOTE.TEXT",
            });

            Assert.Multiple(() =>
            {
                Assert.That(prodosList.GetProperty("kind").GetString(), Is.EqualTo("Prodos"));
                Assert.That(Names(prodosList), Does.Contain("NOTE"));
                Assert.That(prodosFile.GetProperty("text").GetString(), Is.EqualTo("Hi"));
                Assert.That(pascalList.GetProperty("kind").GetString(), Is.EqualTo("Pascal"));
                Assert.That(Names(pascalList), Does.Contain("NOTE.TEXT"));
                Assert.That(pascalFile.GetProperty("text").GetString(), Is.EqualTo("Hi"));
            });
        }
        finally
        {
            DeleteDirectory(sandbox);
        }
    }

    /// <summary>
    /// DOS lookup keeps an exact match, falls back when the fold is unique, and reports both names when it is not.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task ReadFile_DosCase_ExactFallbackAndAmbiguous()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-case-").FullName;
        try
        {
            string lower = WriteBytes(sandbox, "lower.do", CaseVolume(upper: false, lower: true));
            string upper = WriteBytes(sandbox, "upper.do", CaseVolume(upper: true, lower: false));
            string both = WriteBytes(sandbox, "both.do", CaseVolume(upper: true, lower: true));
            await using McpSession session = await McpSession.StartAsync(sandbox);

            JsonElement exact = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = lower,
                ["filename"] = "hello",
            });
            JsonElement fallback = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = upper,
                ["filename"] = "Hello",
            });
            JsonElement ambiguous = await session.CallErrorAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = both,
                ["filename"] = "Hello",
            });

            Assert.Multiple(() =>
            {
                Assert.That(Decode(exact)[0], Is.EqualTo(0x22));
                Assert.That(Decode(fallback)[0], Is.EqualTo(0x11));
                Assert.That(ambiguous.GetProperty("message").GetString(), Does.Contain("HELLO"));
                Assert.That(ambiguous.GetProperty("message").GetString(), Does.Contain("hello"));
            });
        }
        finally
        {
            DeleteDirectory(sandbox);
        }
    }

    /// <summary>
    /// Host paths that leave the sandbox are rejected for mount, list, and read.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Test]
    public async Task Sandbox_RejectsParentAbsoluteAndSymlink()
    {
        string sandbox = Directory.CreateTempSubdirectory("mcp-sandbox-io-").FullName;
        string outside = Directory.CreateTempSubdirectory("mcp-outside-io-").FullName;
        try
        {
            string outsideFile = Path.Join(outside, "secret.do");
            File.WriteAllBytes(outsideFile, "SECRET-OUTSIDE"u8.ToArray());
            string parent = Path.Join(sandbox, "..", Path.GetFileName(outsideFile));
            string link = Path.Join(sandbox, "linked.do");
            CreateLinkOrIgnore(() => File.CreateSymbolicLink(link, outsideFile));
            string inside = WriteDosVolume(sandbox, "inside.do");
            string alias = Path.Join(sandbox, "alias.do");
            CreateLinkOrIgnore(() => File.CreateSymbolicLink(alias, inside));

            await using McpSession session = await McpSession.StartAsync(sandbox);
            JsonElement parentMount = await session.CallErrorAsync("mount_disk", new Dictionary<string, object?> { ["path"] = parent });
            JsonElement absoluteMount = await session.CallErrorAsync("mount_disk", new Dictionary<string, object?> { ["path"] = outsideFile });
            JsonElement linkMount = await session.CallErrorAsync("mount_disk", new Dictionary<string, object?> { ["path"] = link });
            JsonElement parentRead = await session.CallErrorAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = parent,
                ["filename"] = "NOTE",
            });
            JsonElement absoluteRead = await session.CallErrorAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = outsideFile,
                ["filename"] = "NOTE",
            });
            JsonElement linkRead = await session.CallErrorAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = link,
                ["filename"] = "NOTE",
            });
            JsonElement linkList = await session.CallErrorAsync("disk_list_files", new Dictionary<string, object?>
            {
                ["image"] = link,
            });
            JsonElement allowed = await session.CallAsync("disk_read_file", new Dictionary<string, object?>
            {
                ["image"] = alias,
                ["filename"] = "NOTE",
            });

            Assert.Multiple(() =>
            {
                Assert.That(parent, Does.Contain(".."));
                Assert.That(parentMount.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(absoluteMount.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(linkMount.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(parentRead.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(absoluteRead.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(linkRead.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(linkList.GetProperty("message").GetString(), Does.Contain("sandbox"));
                Assert.That(parentRead.GetProperty("message").GetString(), Does.Not.Contain("SECRET-OUTSIDE"));
                Assert.That(linkRead.GetProperty("message").GetString(), Does.Not.Contain("SECRET-OUTSIDE"));
                Assert.That(allowed.GetProperty("text").GetString(), Does.StartWith("HI\n"));
                Assert.That(Directory.GetFiles(outside), Is.EqualTo(new[] { outsideFile }));
                Assert.That(File.ReadAllBytes(outsideFile), Is.EqualTo("SECRET-OUTSIDE"u8.ToArray()));
            });
        }
        finally
        {
            DeleteDirectory(sandbox);
            DeleteDirectory(outside);
        }
    }

    private static byte[] Decode(JsonElement file)
    {
        return Convert.FromBase64String(file.GetProperty("base64").GetString() ?? string.Empty);
    }

    private static IEnumerable<string> Names(JsonElement catalog)
    {
        foreach (JsonElement entry in catalog.GetProperty("entries").EnumerateArray())
        {
            yield return entry.GetProperty("name").GetString() ?? string.Empty;
        }
    }

    private static IEnumerable<string> DisplayNames(JsonElement catalog)
    {
        foreach (JsonElement entry in catalog.GetProperty("entries").EnumerateArray())
        {
            yield return entry.GetProperty("displayName").GetString() ?? string.Empty;
        }
    }

    private static string WriteDosVolume(string directory, string name)
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddFile("NOTE", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0xC8, 0xC9, 0x8D } });
        builder.AddCatalogName(new byte[] { 0x01, 0xC2 }, Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0xC1 } });
        return WriteBytes(directory, name, builder.Build().Image);
    }

    private static ReadOnlyMemory<byte> CaseVolume(bool upper, bool lower)
    {
        var builder = new Dos33ImageBuilder(35, 254);
        if (upper)
        {
            builder.AddFile("HELLO", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        }

        if (lower)
        {
            builder.AddCatalogName(new byte[] { 0xE8, 0xE5, 0xEC, 0xEC, 0xEF }, Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x22 } });
        }

        return builder.Build().Image;
    }

    private static string WriteBytes(string directory, string name, ReadOnlyMemory<byte> image)
    {
        string path = Path.Join(directory, name);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    private static void WriteProfile(string directory, string profileFileName)
    {
        byte[] rom = new byte[RomSize];
        rom[0] = 0xDB;
        rom[0x2FFC] = 0x00;
        rom[0x2FFD] = 0xD0;
        File.WriteAllBytes(Path.Join(directory, "monitor.bin"), rom);
        File.WriteAllText(
            Path.Join(directory, profileFileName),
            """
            {
              "name": "PROFILE_NAME",
              "displayName": "MCP I/O",
              "description": "Generated profile for screen, keyboard, and disk tools",
              "cpu": { "type": "65C02", "clockSpeed": 1000000 },
              "addressSpace": 16,
              "memory": {
                "rom-images": [
                  { "name": "monitor", "source": "monitor.bin", "size": "0x3000", "required": true }
                ],
                "physical": [
                  { "name": "main-ram-48k", "size": "0xC000", "fill": "0x00" },
                  {
                    "name": "system-rom-12k",
                    "size": "0x3000",
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
                    "name": "io-region",
                    "type": "composite",
                    "start": "0xC000",
                    "size": "0x1000",
                    "handler": "composite-io"
                  },
                  {
                    "name": "system-rom",
                    "type": "rom",
                    "start": "0xD000",
                    "size": "0x3000",
                    "permissions": "rx",
                    "source": "system-rom-12k",
                    "source-offset": "0x0000"
                  }
                ]
              },
              "devices": {
                "motherboard": [
                  { "type": "keyboard", "name": "Keyboard", "enabled": true },
                  { "type": "video", "name": "Video", "enabled": true }
                ],
                "slots": {
                  "io-region": "io-region",
                  "enabled": true,
                  "internalC3Rom": false,
                  "internalCxRom": false,
                  "cards": [
                    { "slot": 6, "type": "disk-ii-compatible" }
                  ]
                }
              },
              "boot": { "autoStart": false }
            }
            """.Replace("PROFILE_NAME", Path.GetFileNameWithoutExtension(profileFileName), StringComparison.Ordinal));
    }

    private static void CreateLinkOrIgnore(Action create)
    {
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not available: {ex.Message}");
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A mounted image can keep its file open until the host finishes releasing it.
        }
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

        private async Task<JsonElement> CallCoreAsync(string name, Dictionary<string, object?>? arguments, bool expectError)
        {
            if (!expectError)
            {
                EnsurePowered(this.host, name);
            }

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