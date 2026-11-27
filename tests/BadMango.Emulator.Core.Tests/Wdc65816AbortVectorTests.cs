// <copyright file="Wdc65816AbortVectorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

/// <summary>
/// Documents the 65816 ABORT vector addresses from section 11.2 of the bus fast-path spec.
/// </summary>
/// <remarks>
/// The vector sequence is a requirement on a 65816 CPU. This test records the addresses.
/// It does not push a vector, change registers, or implement that CPU.
/// </remarks>
[TestFixture]
public class Wdc65816AbortVectorTests
{
    /// <summary>
    /// Bank 0 emulation vector, low byte. The high byte is the next address.
    /// </summary>
    private const int EmulationVector = 0xFFF8;

    /// <summary>
    /// Bank 0 native vector, low byte. The high byte is the next address.
    /// </summary>
    private const int NativeVector = 0xFFE8;

    /// <summary>
    /// Section 11.2: emulation <c>$FFF8</c>/<c>$FFF9</c>, native <c>$FFE8</c>/<c>$FFE9</c>.
    /// ABORTB aborts the current instruction without modifying the registers that instruction
    /// was about to write. The address pushed is the address of the aborted instruction, and
    /// RTI returns there.
    /// </summary>
    [Test]
    public void Section11_2_DocumentsBank0AbortVectors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EmulationVector, Is.EqualTo(0xFFF8));
            Assert.That(EmulationVector + 1, Is.EqualTo(0xFFF9));
            Assert.That(NativeVector, Is.EqualTo(0xFFE8));
            Assert.That(NativeVector + 1, Is.EqualTo(0xFFE9));
            Assert.That(NativeVector, Is.LessThan(EmulationVector));
        });
    }
}