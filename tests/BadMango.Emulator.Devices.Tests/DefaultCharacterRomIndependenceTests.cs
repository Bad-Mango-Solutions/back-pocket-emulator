// <copyright file="DefaultCharacterRomIndependenceTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Moq;

/// <summary>
/// Two machines each copy <see cref="DefaultCharacterRom.RomData"/> into their own character device (§14.33).
/// </summary>
[TestFixture]
public class DefaultCharacterRomIndependenceTests
{
    /// <summary>
    /// Writing one machine's character ROM does not change the other machine's copy or the shared span.
    /// </summary>
    [Test]
    public void RomData_CopiedIntoTwoMachines_IsIndependent()
    {
        var deviceA = new CharacterDevice();
        var deviceB = new CharacterDevice();
        using IMachine machineA = CreateMachine(deviceA);
        using IMachine machineB = CreateMachine(deviceB);

        ICharacterDevice loadedA = machineA.GetComponent<ICharacterDevice>()!;
        ICharacterDevice loadedB = machineB.GetComponent<ICharacterDevice>()!;
        loadedA.LoadCharacterRom(DefaultCharacterRom.RomData.ToArray());
        loadedB.LoadCharacterRom(DefaultCharacterRom.RomData.ToArray());

        byte original = DefaultCharacterRom.RomData[0];
        byte mutated = (byte)~original;
        loadedA.GetCharacterRomData().Span[0] = mutated;

        Assert.Multiple(() =>
        {
            Assert.That(loadedA.GetCharacterRomData().Span[0], Is.EqualTo(mutated));
            Assert.That(loadedB.GetCharacterRomData().Span[0], Is.EqualTo(original));
            Assert.That(DefaultCharacterRom.RomData[0], Is.EqualTo(original));
        });
    }

    private static IMachine CreateMachine(CharacterDevice characterDevice)
    {
        var cpu = new Mock<ICpu>();
        cpu.Setup(mock => mock.Halted).Returns(false);
        cpu.Setup(mock => mock.IsStopRequested).Returns(false);
        cpu.Setup(mock => mock.Step()).Returns(new CpuStepResult(CpuRunState.Running, 1));

        return new MachineBuilder()
            .WithCpuFactory(_ => cpu.Object)
            .AddComponent(characterDevice)
            .Build();
    }
}