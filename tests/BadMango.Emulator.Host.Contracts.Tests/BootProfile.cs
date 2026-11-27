// <copyright file="BootProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// The minimal 65C02 profile that maps Josh's embedded <c>boot.rom</c>.
/// </summary>
internal static class BootProfile
{
    /// <summary>
    /// The profile id stored on the host.
    /// </summary>
    internal const string Id = "boot-rom-test";

    private const string Json = """
        {
            "name": "boot-rom-test",
            "displayName": "Boot ROM Test",
            "description": "Minimal 65C02 profile that boots from embedded boot.rom.",
            "cpu": { "type": "65C02", "clockSpeed": 1020484 },
            "addressSpace": 16,
            "memory": {
                "rom-images": [
                    {
                        "name": "boot-rom",
                        "source": "embedded://BadMango.Emulator.Devices/BadMango.Emulator.Devices.Resources.boot.rom",
                        "size": "0x0800",
                        "required": true,
                        "on_verification_fail": "stop"
                    }
                ],
                "physical": [
                    { "name": "main-ram", "size": "0xF000", "fill": "0x00" },
                    {
                        "name": "system-rom",
                        "size": "0x1000",
                        "fill": "0x00",
                        "sources": [{ "type": "rom-image", "name": "boot", "rom-image": "boot-rom", "offset": "0x0800" }]
                    }
                ],
                "regions": [
                    {
                        "name": "ram",
                        "type": "ram",
                        "start": "0x0000",
                        "size": "0xF000",
                        "permissions": "rwx",
                        "source": "main-ram",
                        "source-offset": "0x0000"
                    },
                    {
                        "name": "rom",
                        "type": "rom",
                        "start": "0xF000",
                        "size": "0x1000",
                        "permissions": "rx",
                        "source": "system-rom",
                        "source-offset": "0x0000"
                    }
                ]
            }
        }
        """;

    /// <summary>
    /// Gets the profile document stored by <see cref="Ensure"/>.
    /// </summary>
    internal static string Document => Json;

    /// <summary>
    /// Stores the profile if this host does not already have it.
    /// </summary>
    /// <param name="control">The control plane.</param>
    internal static void Ensure(IHostControl control)
    {
        control.UpsertProfile(new HostProfileDocument(Id, "Boot ROM Test", Json));
    }

    /// <summary>
    /// Returns the boot document with one extra root property inserted before the closing brace.
    /// </summary>
    /// <param name="property">A JSON property, including its name, such as <c>"guestOs": { "callLog": "nope" }</c>.</param>
    /// <returns>The profile JSON.</returns>
    internal static string WithExtra(string property)
    {
        int end = Json.LastIndexOf('}');
        return Json[..end] + ", " + property + "}";
    }
}