// <copyright file="MachineProfileSchemaTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using System.Text.Json.Nodes;

using BadMango.Emulator.Host;

/// <summary>
/// Schema keyword coverage and document checks for <see cref="MachineProfileSchema"/>.
/// </summary>
[TestFixture]
public class MachineProfileSchemaTests
{
    /// <summary>
    /// The embedded machine profile schema uses only keywords the checker implements.
    /// </summary>
    [Test]
    public void EmbeddedSchema_Loads()
    {
        Assert.That(MachineProfileSchema.EnsureEmbeddedSchema, Throws.Nothing);
    }

    /// <summary>
    /// A schema keyword the checker does not implement fails at load and names the keyword and path.
    /// </summary>
    [Test]
    public void UnsupportedKeyword_FailsAtLoad()
    {
        JsonObject schema = JsonNode.Parse("""{"oneOf":[{"type":"string"}]}""")!.AsObject();

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(
            () => MachineProfileSchema.EnsureSupported(schema));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("oneOf"));
            Assert.That(error.Message, Does.Contain("$.oneOf"));
        });
    }

    /// <summary>
    /// The boot profile and a profile that exercises the schema's nested rules both pass.
    /// </summary>
    [Test]
    public void ValidProfiles_Pass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => MachineProfileSchema.Validate(BootProfile.Document), Throws.Nothing);
            Assert.That(() => MachineProfileSchema.Validate(SampleProfile()), Throws.Nothing);
        });
    }

    /// <summary>
    /// Each invalid document names the location that failed.
    /// </summary>
    [Test]
    public void InvalidDocuments_NameTheLocation()
    {
        AssertInvalid(SampleProfile().Replace("\"name\": \"schema-sample\",", string.Empty, StringComparison.Ordinal), "$.name");
        AssertInvalid(SampleProfile().Replace("\"65C02\"", "\"z80\"", StringComparison.Ordinal), "$.cpu.type");
        AssertInvalid(SampleProfile().Replace("\"schema-sample\"", "\"Bad_Name\"", StringComparison.Ordinal), "$.name");
        AssertInvalid(
            SampleProfile().Replace("\"callLog\": \"off\"", "\"callLog\": \"off\", \"extra\": true", StringComparison.Ordinal),
            "$.guestOs.extra");
        AssertInvalid(SampleProfile().Replace("\"addressSpace\": 16", "\"addressSpace\": 4", StringComparison.Ordinal), "$.addressSpace");
        AssertInvalid(SampleProfile().Replace("\"start\": \"0x0000\"", "\"start\": \"main\"", StringComparison.Ordinal), "$.memory.regions[0].start");
    }

    private static void AssertInvalid(string json, string location)
    {
        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => MachineProfileSchema.Validate(json));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("schemas/machine-profile.schema.json"));
            Assert.That(error.Message, Does.Contain(location));
        });
    }

    private static string SampleProfile()
    {
        return """
            {
                "name": "schema-sample",
                "cpu": { "type": "65C02", "clockSpeed": 1020484 },
                "addressSpace": 16,
                "memory": {
                    "regions": [
                        { "name": "ram", "type": "ram", "start": "0x0000", "size": "0x1000", "permissions": "rwx" }
                    ]
                },
                "devices": {
                    "slots": {
                        "enabled": true,
                        "cards": [ { "slot": 6, "type": "disk-ii-compatible", "config": { "timing": "instant" } } ]
                    }
                },
                "boot": { "startupSlot": 6, "powerOnMemory": { "pattern": "zero", "seed": 1 } },
                "guestOs": { "detect": false, "callLog": "off" }
            }
            """;
    }
}