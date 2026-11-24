// <copyright file="GuestOsModuleTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using Autofac;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Guest-OS services are absent unless the module is enabled, and ProDOS is registered before DOS.
/// </summary>
[TestFixture]
public class GuestOsModuleTests
{
    /// <summary>The default debug command module does not register guest-OS services.</summary>
    [Test]
    public void DefaultDebugCommands_DoNotRegisterGuestOsServices()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<DeviceDebugCommandsModule>();
        using var container = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(container.IsRegistered<IGuestOsDetector>(), Is.False);
            Assert.That(container.IsRegistered<IGuestOsProbe>(), Is.False);
            Assert.That(container.IsRegistered<IBootSequenceMonitor>(), Is.False);
            Assert.That(container.IsRegistered<IGuestMemoryReader>(), Is.False);
        });
    }

    /// <summary>A disabled module registers nothing.</summary>
    [Test]
    public void DisabledModule_RegistersNothing()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new GuestOsModule());
        using var container = builder.Build();

        Assert.That(container.IsRegistered<IGuestOsDetector>(), Is.False);
        Assert.That(container.IsRegistered<IGuestOsProbe>(), Is.False);
    }

    /// <summary>An enabled module orders ProDOS ahead of DOS and detects through the supplied reader.</summary>
    [Test]
    public void EnabledModule_OrdersProDosBeforeDos_AndDetects()
    {
        var image = new ArrayGuestMemory();
        image.Poke(0xBF00, 0x4C);
        image.Poke(0xBF06, 0x60);
        image.Poke(0xBF07, 0x00);
        image.Poke(0xBF08, 0xD0);
        image.Poke(0xBF98, 0x01);
        image.Poke(0xBFFD, 0x07);
        image.Poke(0xBFFF, 0x24);
        image.PlantDosPage3();

        var builder = new ContainerBuilder();
        builder.RegisterInstance(image).As<IGuestMemoryReader>().SingleInstance();
        builder.RegisterModule(new GuestOsModule(enabled: true));
        using var container = builder.Build();

        var probes = container.Resolve<IEnumerable<IGuestOsProbe>>().ToList();
        var detector = container.Resolve<IGuestOsDetector>();
        var info = detector.Detect();

        Assert.Multiple(() =>
        {
            Assert.That(probes, Has.Count.EqualTo(2));
            Assert.That(probes[0], Is.InstanceOf<ProDos8GuestOsProbe>());
            Assert.That(probes[1], Is.InstanceOf<Dos33GuestOsProbe>());
            Assert.That(probes[0].Order, Is.LessThan(probes[1].Order));
            Assert.That(info.Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(info.VersionLabel, Is.EqualTo("ProDOS 8 2.4.x"));
            Assert.That(((ProDos8Details)info.Details!).HasClock, Is.True);
        });
    }

    private sealed class ArrayGuestMemory : IGuestMemoryReader
    {
        private readonly byte[] image = new byte[65536];

        public byte Read(uint address) => address >= (uint)this.image.Length ? (byte)0xFF : this.image[address];

        public void Poke(ushort address, byte value) => this.image[address] = value;

        public void PlantDosPage3()
        {
            this.PlantJmp(0x03D0, 0x9DBF);
            this.PlantJmp(0x03D3, 0x9D84);
            this.PlantJmp(0x03D6, 0xAAFD);
            this.PlantJmp(0x03D9, 0xB7B5);
            this.PlantJmp(0x03EA, 0xA851);
        }

        private void PlantJmp(ushort address, ushort target)
        {
            this.Poke(address, 0x4C);
            this.Poke((ushort)(address + 1), (byte)target);
            this.Poke((ushort)(address + 2), (byte)(target >> 8));
        }
    }
}