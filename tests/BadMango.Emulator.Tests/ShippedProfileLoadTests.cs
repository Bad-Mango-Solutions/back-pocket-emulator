// <copyright file="ShippedProfileLoadTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Debug.Infrastructure;

/// <summary>
/// Loads every shipped profile through <see cref="MachineFactory.CreateMachine"/>.
/// </summary>
[TestFixture]
public sealed class ShippedProfileLoadTests
{
    /// <summary>
    /// Verifies that each file under <c>profiles/</c> builds a machine.
    /// </summary>
    /// <param name="profileFileName">The profile file name, such as <c>pocket2e.json</c>.</param>
    [TestCaseSource(nameof(ShippedProfileFileNames))]
    public void ShippedProfile_LoadsThroughMachineFactory(string profileFileName)
    {
        string profilesDirectory = Path.Join(LocateRepositoryRoot(), "profiles");
        string profilePath = Path.Join(profilesDirectory, profileFileName);
        string libraryRoot = Path.Join(Path.GetTempPath(), "bpe-profile-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(libraryRoot);

        try
        {
            var loader = new MachineProfileLoader(profilesDirectory);
            MachineProfile profile = loader.LoadProfileFromFile(profilePath);
            var resolver = new ProfilePathResolver(libraryRoot, profilePath);
            WriteBlankLibraryRoms(profile, resolver, libraryRoot);

            using IMachine machine = MachineFactory.CreateMachine(profile, resolver);

            Assert.Multiple(() =>
            {
                Assert.That(machine.Cpu, Is.Not.Null);
                Assert.That(machine.Bus, Is.Not.Null);
            });
        }
        finally
        {
            Directory.Delete(libraryRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that <c>internalC3Rom</c> and <c>internalCxRom</c> are the composite handler's
    /// initial state, including after power-on when the extended 80-column card is present.
    /// </summary>
    /// <param name="internalC3Rom">The profile's internal $C300 ROM selection.</param>
    /// <param name="internalCxRom">The profile's internal $C100-$CFFF ROM selection.</param>
    /// <param name="includeExtended80Column">Whether the profile installs the extended 80-column card.</param>
    /// <returns>A task that completes when the machine has powered on.</returns>
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    public async Task FromProfile_AppliesSlotInternalRomSelection(
        bool internalC3Rom,
        bool internalCxRom,
        bool includeExtended80Column)
    {
        MachineProfile profile = CreateSlotRomProfile(internalC3Rom, internalCxRom, includeExtended80Column);

        using IMachine machine = MachineFactory.CreateMachine(profile);
        IInternalRomHandler? handler = machine.GetComponent<IInternalRomHandler>();
        Assert.That(handler, Is.Not.Null);

        AssertSlotRomSelection(handler!, internalC3Rom, internalCxRom);

        await machine.PowerOnAsync();

        AssertSlotRomSelection(handler!, internalC3Rom, internalCxRom);
    }

    private static void AssertSlotRomSelection(IInternalRomHandler handler, bool internalC3Rom, bool internalCxRom)
    {
        Assert.Multiple(() =>
        {
            Assert.That(handler.IsIntC3RomEnabled, Is.EqualTo(internalC3Rom));
            Assert.That(handler.IsIntCxRomEnabled, Is.EqualTo(internalCxRom));
        });
    }

    private static MachineProfile CreateSlotRomProfile(
        bool internalC3Rom,
        bool internalCxRom,
        bool includeExtended80Column)
    {
        List<MotherboardDeviceEntry>? motherboard = null;
        if (includeExtended80Column)
        {
            motherboard =
            [
                new MotherboardDeviceEntry
                {
                    Type = "extended80column",
                    Name = "Extended 80-Column Card",
                    Enabled = true,
                },
            ];
        }

        return new MachineProfile
        {
            Name = "slot-rom-selection",
            Cpu = new CpuProfileSection
            {
                Type = "65C02",
            },
            AddressSpace = 16,
            Memory = new MemoryProfileSection
            {
                Physical =
                [
                    new PhysicalMemoryProfile
                    {
                        Name = "main-ram",
                        Size = "0x10000",
                        Fill = "0x00",
                    },
                ],
                Regions =
                [
                    new MemoryRegionProfile
                    {
                        Name = "main-ram",
                        Type = "ram",
                        Start = "0x0000",
                        Size = "0xC000",
                        Permissions = "rwx",
                        Source = "main-ram",
                        SourceOffset = "0x0000",
                    },
                    new MemoryRegionProfile
                    {
                        Name = "io-page",
                        Type = "composite",
                        Start = "0xC000",
                        Size = "0x1000",
                        Handler = "composite-io",
                    },
                ],
            },
            Devices = new DevicesProfile
            {
                Motherboard = motherboard,
                Slots = new SlotSystemProfile
                {
                    Enabled = true,
                    InternalC3Rom = internalC3Rom,
                    InternalCxRom = internalCxRom,
                    Cards = [],
                },
            },
        };
    }

    private static void WriteBlankLibraryRoms(
        MachineProfile profile,
        ProfilePathResolver resolver,
        string libraryRoot)
    {
        if (profile.Memory.RomImages is null)
        {
            return;
        }

        foreach (RomImageProfile romImage in profile.Memory.RomImages)
        {
            WriteBlankLibraryRom(resolver, libraryRoot, romImage);
        }
    }

    private static void WriteBlankLibraryRom(
        ProfilePathResolver resolver,
        string libraryRoot,
        RomImageProfile romImage)
    {
        if (ProfilePathResolver.IsEmbeddedResource(romImage.Source))
        {
            return;
        }

        if (!romImage.Source.StartsWith("library://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ROM '{romImage.Name}' uses '{romImage.Source}', which this test does not read.");
        }

        string resolvedPath = Path.GetFullPath(resolver.Resolve(romImage.Source));
        string libraryPrefix = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(libraryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ROM '{romImage.Name}' resolved outside the temporary library.");
        }

        string? directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        uint size = HexParser.ParseUInt32(romImage.Size);
        using var stream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(size);
    }

    private static IEnumerable<string> ShippedProfileFileNames()
    {
        string profilesDirectory = Path.Join(LocateRepositoryRoot(), "profiles");
        IEnumerable<string> profilePaths = Directory.EnumerateFiles(profilesDirectory, "*.json")
            .OrderBy(static filePath => filePath, StringComparer.Ordinal);
        foreach (string profilePath in profilePaths)
        {
            string? fileName = Path.GetFileName(profilePath);
            if (!string.IsNullOrEmpty(fileName))
            {
                yield return fileName;
            }
        }
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "BackPocketEmulator.slnx")) &&
                Directory.Exists(Path.Join(dir.FullName, "profiles")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}