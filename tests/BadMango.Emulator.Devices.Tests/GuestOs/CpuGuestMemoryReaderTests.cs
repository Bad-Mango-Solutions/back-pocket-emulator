// <copyright file="CpuGuestMemoryReaderTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.GuestOs;

using Moq;

/// <summary>
/// <see cref="CpuGuestMemoryReader"/> reads with <see cref="ICpu.Peek8"/> and nothing else.
/// </summary>
[TestFixture]
public class CpuGuestMemoryReaderTests
{
    /// <summary>A probe read is a peek. It does not fetch through the bus with side effects.</summary>
    [Test]
    public void Read_UsesPeek8_AndDoesNotCallRead8()
    {
        var cpu = new Mock<ICpu>(MockBehavior.Strict);
        cpu.Setup(candidate => candidate.Peek8(0xBF00)).Returns(0x4C);
        var reader = new CpuGuestMemoryReader(cpu.Object);

        var value = reader.Read(0xBF00);

        Assert.That(value, Is.EqualTo(0x4C));
        cpu.Verify(candidate => candidate.Peek8(0xBF00), Times.Once);
        cpu.Verify(candidate => candidate.Read8(It.IsAny<Addr>()), Times.Never);
        cpu.Verify(candidate => candidate.Write8(It.IsAny<Addr>(), It.IsAny<byte>()), Times.Never);
    }

    /// <summary>A null CPU is rejected.</summary>
    [Test]
    public void Constructor_NullCpu_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CpuGuestMemoryReader(null!));
    }
}