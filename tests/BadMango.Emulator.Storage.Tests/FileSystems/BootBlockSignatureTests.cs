// <copyright file="BootBlockSignatureTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

/// <summary>
/// The boot signatures disk info and the guest-OS boot path share.
/// </summary>
[TestFixture]
public class BootBlockSignatureTests
{
    /// <summary>DOS 3.3 boot 1 begins with <c>01 A5 27 C9 09</c>.</summary>
    [Test]
    public void Dos33BootSector_Signature_IsTheBoot1Prefix()
    {
        Assert.That(Dos33Layout.BootSector.Signature.ToArray(), Is.EqualTo(new byte[] { 0x01, 0xA5, 0x27, 0xC9, 0x09 }));
    }

    /// <summary>The ProDOS boot block begins with <c>01 38 B0 03</c>.</summary>
    [Test]
    public void ProdosBootBlock_Signature_IsThePbootPrefix()
    {
        Assert.That(ProdosLayout.BootBlock.Signature.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x38, 0xB0, 0x03 }));
    }
}