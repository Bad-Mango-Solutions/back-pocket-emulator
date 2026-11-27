// <copyright file="FaultKindTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

/// <summary>
/// Unit tests for the <see cref="FaultKind"/> enum.
/// </summary>
[TestFixture]
public class FaultKindTests
{
    /// <summary>
    /// Verifies that None has value 0.
    /// </summary>
    [Test]
    public void FaultKind_None_HasValueZero()
    {
        Assert.That((byte)FaultKind.None, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that all expected fault kinds exist.
    /// </summary>
    [Test]
    public void FaultKind_ContainsExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(FaultKind.None), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Unmapped), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Permission), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Nx), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Misaligned), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.DeviceFault), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Write), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Fetch), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.BankCrossing), Is.True);
            Assert.That(Enum.IsDefined(FaultKind.Privilege), Is.True);
        });
    }

    /// <summary>
    /// Verifies the enum has exactly ten values and the documented mask bit values.
    /// </summary>
    [Test]
    public void FaultKind_HasExpectedMaskValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That((ushort)FaultKind.None, Is.EqualTo(0x0000));
            Assert.That((ushort)FaultKind.Unmapped, Is.EqualTo(0x0001));
            Assert.That((ushort)FaultKind.Permission, Is.EqualTo(0x0002));
            Assert.That((ushort)FaultKind.Nx, Is.EqualTo(0x0004));
            Assert.That((ushort)FaultKind.Misaligned, Is.EqualTo(0x0008));
            Assert.That((ushort)FaultKind.DeviceFault, Is.EqualTo(0x0010));
            Assert.That((ushort)FaultKind.Write, Is.EqualTo(0x0100));
            Assert.That((ushort)FaultKind.Fetch, Is.EqualTo(0x0200));
            Assert.That((ushort)FaultKind.BankCrossing, Is.EqualTo(0x0400));
            Assert.That((ushort)FaultKind.Privilege, Is.EqualTo(0x0800));

            Assert.That((byte)FaultKind.None, Is.EqualTo(0x00));
            Assert.That((byte)FaultKind.Unmapped, Is.EqualTo(0x01));
            Assert.That((byte)FaultKind.Permission, Is.EqualTo(0x02));
            Assert.That((byte)FaultKind.Nx, Is.EqualTo(0x04));
            Assert.That((byte)FaultKind.Misaligned, Is.EqualTo(0x08));
            Assert.That((byte)FaultKind.DeviceFault, Is.EqualTo(0x10));
            Assert.That((byte)((ushort)FaultKind.Write & 0xFF), Is.EqualTo(0x00));
            Assert.That((byte)((ushort)FaultKind.Fetch & 0xFF), Is.EqualTo(0x00));
            Assert.That((byte)((ushort)FaultKind.BankCrossing & 0xFF), Is.EqualTo(0x00));
            Assert.That((byte)((ushort)FaultKind.Privilege & 0xFF), Is.EqualTo(0x00));
        });

        var values = Enum.GetValues<FaultKind>();
        Assert.That(values, Has.Length.EqualTo(10));
    }
}