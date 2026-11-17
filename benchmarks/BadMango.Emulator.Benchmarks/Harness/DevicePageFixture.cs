// <copyright file="DevicePageFixture.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Harness;

using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Systems;

/// <summary>
/// A headless Pocket2e with a language card, an extended 80-column card, and a test ROM.
/// </summary>
/// <remarks>
/// <para>
/// The memory map follows the machine tests that install the 80-column card: 48 KB of main
/// RAM registered as <c>main-ram-48k</c>, so page 0 is the card's composite target and
/// 80STORE / PAGE2 can move text page 1. The language card and the I/O page are the same
/// pieces <c>AsPocket2e</c> installs. RAM and ROM bytes are test values. Nothing is read
/// from disk.
/// </para>
/// <para>
/// <see cref="VideoDevice"/> is not installed. It would register the same PAGE2 switches and
/// also schedule a vertical-blank event, and that event allocates when the CPU advances the
/// scheduler. This fixture registers <c>$C054</c> and <c>$C055</c> and calls
/// <see cref="Extended80ColumnDevice.SetPage2(bool)"/>, which is how the video device remaps
/// text page 1.
/// </para>
/// </remarks>
public sealed class DevicePageFixture
{
    private const int MainRamBytes = 0xC000;

    private const Addr RomBase = 0xD000;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevicePageFixture"/> class and resets the CPU.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a required component is missing.</exception>
    public DevicePageFixture()
    {
        Machine = Build();
        Cpu = Machine.Cpu as Cpu65C02
            ?? throw new InvalidOperationException("Device-page machine CPU should be a 65C02.");
        LanguageCard = Machine.GetComponent<LanguageCardDevice>()
            ?? throw new InvalidOperationException("Language card was not installed.");
        Column = Machine.GetComponent<Extended80ColumnDevice>()
            ?? throw new InvalidOperationException("Extended 80-column card was not installed.");
        Cpu.Reset();
    }

    /// <summary>
    /// Gets the assembled machine.
    /// </summary>
    public IMachine Machine { get; }

    /// <summary>
    /// Gets the 65C02 that executes the workload.
    /// </summary>
    public Cpu65C02 Cpu { get; }

    /// <summary>
    /// Gets the language card. Its soft switches remap <c>$D000</c>–<c>$FFFF</c>.
    /// </summary>
    public LanguageCardDevice LanguageCard { get; }

    /// <summary>
    /// Gets the extended 80-column card. 80STORE and PAGE2 remap text page 1 through it.
    /// </summary>
    public Extended80ColumnDevice Column { get; }

    private static IMachine Build()
    {
        var mainRam = new PhysicalMemory(MainRamBytes, "main-ram-48k");
        var builder = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpu(CpuFamily.Cpu65C02)
            .WithCpuFactory(context => new Cpu65C02(context));
        builder.AddPhysicalMemory("main-ram-48k", mainRam);
        builder.ConfigureMemory((bus, registry) =>
        {
            PhysicalMemory ram = builder.GetPhysicalMemory("main-ram-48k")
                ?? throw new InvalidOperationException("main-ram-48k was not registered.");
            var target = new RamTarget(ram.Slice(0, MainRamBytes), "main");
            int deviceId = registry.GenerateId();
            registry.Register(deviceId, "RAM", "Main RAM", "Memory/MainRAM");
            bus.MapRegion(
                0x0000,
                MainRamBytes,
                deviceId,
                RegionTag.Ram,
                PagePerms.All,
                target.Capabilities,
                target,
                0);
        });

        return builder
            .WithLanguageCard()
            .WithSlotManager()
            .WithPocket2eIOPage()
            .WithExtended80ColumnDevice()
            .WithRom(DevicePageWorkload.CreateRomImage(), RomBase, "device-page-rom")
            .AfterSoftSwitchHandlerRegistration(WirePage2)
            .Build();
    }

    private static void WirePage2(IMachine machine)
    {
        var column = machine.GetComponent<Extended80ColumnDevice>()
            ?? throw new InvalidOperationException("Extended 80-column card was not installed.");
        var dispatcher = machine.GetComponent<IOPageDispatcher>()
            ?? throw new InvalidOperationException("I/O page dispatcher was not installed.");

        dispatcher.Register(
            0x54,
            (byte offset, in BusAccess context) => SelectPage(column, offset, context, selected: false),
            (byte offset, byte value, in BusAccess context) => SelectPage(column, offset, value, context, selected: false));
        dispatcher.Register(
            0x55,
            (byte offset, in BusAccess context) => SelectPage(column, offset, context, selected: true),
            (byte offset, byte value, in BusAccess context) => SelectPage(column, offset, value, context, selected: true));
    }

    private static byte SelectPage(Extended80ColumnDevice column, byte offset, in BusAccess context, bool selected)
    {
        _ = offset;
        if (!context.IsSideEffectFree)
        {
            column.SetPage2(selected);
        }

        return 0xFF;
    }

    private static void SelectPage(Extended80ColumnDevice column, byte offset, byte value, in BusAccess context, bool selected)
    {
        _ = offset;
        _ = value;
        if (!context.IsSideEffectFree)
        {
            column.SetPage2(selected);
        }
    }
}