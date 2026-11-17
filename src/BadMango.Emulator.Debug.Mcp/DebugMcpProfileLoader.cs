// <copyright file="DebugMcpProfileLoader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;

using SessionMachineInfo = BadMango.Emulator.Debug.Abstractions.MachineInfo;

/// <summary>
/// Loads a machine profile into the shared debug context.
/// </summary>
public sealed class DebugMcpProfileLoader
{
    private readonly IDebugContext context;
    private readonly IDebugSession session;
    private readonly IMachineProfileLoader loader;
    private readonly SandboxPathGuard sandbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugMcpProfileLoader"/> class.
    /// </summary>
    /// <param name="context">The debug context that owns the machine.</param>
    /// <param name="session">The session that reports machine info after the load.</param>
    /// <param name="loader">The profile deserializer.</param>
    /// <param name="sandbox">The sandbox used for profiles that are not shipped with the host.</param>
    public DebugMcpProfileLoader(
        IDebugContext context,
        IDebugSession session,
        IMachineProfileLoader loader,
        SandboxPathGuard sandbox)
    {
        this.context = context;
        this.session = session;
        this.loader = loader;
        this.sandbox = sandbox;
    }

    /// <summary>
    /// Loads <paramref name="name"/> and rebuilds the machine.
    /// </summary>
    /// <param name="name">A profile name, or an absolute profile path under a sandbox root.</param>
    /// <returns>The machine description after the load.</returns>
    public SessionMachineInfo Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string profilePath = this.ResolveProfilePath(name, out bool sandboxed);
        if (sandboxed)
        {
            this.sandbox.EnsureAllowed(profilePath);
        }

        MachineProfile profile = this.loader.LoadProfileFromFile(profilePath);
        string? libraryRoot = sandboxed ? null : LibraryRoot();
        var resolver = new ProfilePathResolver(libraryRoot, profilePath);
        if (sandboxed)
        {
            this.EnsureRomSourcesAllowed(profile, resolver);
        }

        if (this.context is not DebugContext debugContext)
        {
            throw new InvalidOperationException("The debug context cannot rebuild the machine.");
        }

        (IMachine machine, IDisassembler disassembler, BadMango.Emulator.Core.Configuration.MachineInfo info) =
            MachineFactory.CreateDebugSystem(profile, resolver);

        debugContext.DetachSystem();
        var tracingListener = new TracingDebugListener();
        machine.Cpu.AttachDebugger(tracingListener);
        debugContext.AttachMachine(machine, disassembler, info, tracingListener);
        return this.session.GetMachineInfo();
    }

    private static string LibraryRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".backpocket");
    }

    private static bool IsProfileName(string name)
    {
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !name.Contains("..", StringComparison.Ordinal)
            && !name.Contains('/', StringComparison.Ordinal)
            && !name.Contains('\\', StringComparison.Ordinal);
    }

    private string ResolveProfilePath(string name, out bool sandboxed)
    {
        if (Path.IsPathRooted(name))
        {
            sandboxed = true;
            return Path.GetFullPath(name);
        }

        if (!IsProfileName(name))
        {
            throw new ArgumentException($"Invalid profile name '{name}'.");
        }

        string builtIn = Path.Combine(AppContext.BaseDirectory, "profiles", name + ".json");
        if (File.Exists(builtIn))
        {
            sandboxed = false;
            return Path.GetFullPath(builtIn);
        }

        foreach (string root in this.sandbox.Roots)
        {
            string direct = Path.Combine(root, name + ".json");
            if (File.Exists(direct))
            {
                sandboxed = true;
                return Path.GetFullPath(direct);
            }

            string nested = Path.Combine(root, "profiles", name + ".json");
            if (File.Exists(nested))
            {
                sandboxed = true;
                return Path.GetFullPath(nested);
            }
        }

        throw new FileNotFoundException($"Profile '{name}' was not found.");
    }

    private void EnsureRomSourcesAllowed(MachineProfile profile, ProfilePathResolver resolver)
    {
        if (profile.Memory.RomImages is null)
        {
            return;
        }

        foreach (RomImageProfile rom in profile.Memory.RomImages)
        {
            if (ProfilePathResolver.IsEmbeddedResource(rom.Source))
            {
                continue;
            }

            this.sandbox.EnsureAllowed(resolver.Resolve(rom.Source));
        }
    }
}