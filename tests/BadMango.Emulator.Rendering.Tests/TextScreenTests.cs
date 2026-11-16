// <copyright file="TextScreenTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Rendering.Tests;

using BadMango.Emulator.Rendering;

/// <summary>
/// Locks the shared Apple II text-page map and glyph decode used by the renderer and the debug session.
/// </summary>
[TestFixture]
public sealed class TextScreenTests
{
    /// <summary>Page 1 row addresses follow the hardware text map.</summary>
    [Test]
    public void Page1RowAddress_MatchesHardwareMap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextScreen.Page1RowAddress(0), Is.EqualTo(0x0400));
            Assert.That(TextScreen.Page1RowAddress(1), Is.EqualTo(0x0480));
            Assert.That(TextScreen.Page1RowAddress(8), Is.EqualTo(0x0428));
            Assert.That(TextScreen.Page1RowAddress(16), Is.EqualTo(0x0450));
        });
    }

    /// <summary>40-column page 2, inverse, and flashing codes decode to their glyphs.</summary>
    [Test]
    public void ReadLines_40ColumnPage2_DecodesInverseAndFlashing()
    {
        byte[] ram = new byte[0x10000];
        ram[0x0800] = 0x01;
        ram[0x0801] = 0x41;
        ram[0x0802] = 0xC3;

        string[] lines = TextScreen.ReadLines(address => ram[address], readAux: null, is80Column: false, isPage2: true);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Has.Length.EqualTo(TextScreen.Columns40));
            Assert.That(lines, Has.Length.EqualTo(TextScreen.Rows));
            Assert.That(lines[0][..3], Is.EqualTo("AAC"));
            Assert.That(TextScreen.IsInverse(0x01), Is.True);
            Assert.That(TextScreen.IsFlashing(0x41), Is.True);
            Assert.That(TextScreen.DecodeCharacter(0xC1), Is.EqualTo('A'));
        });
    }

    /// <summary>80-column text interleaves auxiliary even columns with main odd columns and ignores page 2.</summary>
    [Test]
    public void ReadLines_80Column_InterleavesAuxAndMainOnPage1()
    {
        byte[] main = new byte[0x10000];
        byte[] aux = new byte[0x10000];
        main[0x0400] = 0xC2;
        aux[0x0400] = 0xC1;
        main[0x0800] = 0xD8;
        aux[0x0800] = 0xD8;

        string[] lines = TextScreen.ReadLines(
            address => main[address],
            address => aux[address],
            is80Column: true,
            isPage2: true);

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Has.Length.EqualTo(TextScreen.Columns80));
            Assert.That(lines[0][..2], Is.EqualTo("AB"));
        });
    }
}