// <copyright file="KeystrokeLookupTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// Tests the reverse character-to-keystroke table built from <see cref="AppleIIeKeyTranslator"/>.
/// </summary>
[TestFixture]
public sealed class KeystrokeLookupTests
{
    private static readonly AppleIIeKeyTranslator Translator = new();
    private readonly KeystrokeLookup lookup = new(Translator);

    /// <summary>
    /// Verifies that every printable ASCII character from <c>$20</c> to <c>$7E</c>, plus CR, Tab,
    /// and Backspace, maps to a keystroke the translator turns back into the same code.
    /// </summary>
    /// <param name="character">The character to look up.</param>
    [TestCaseSource(nameof(MappedCharacters))]
    public void TryGet_PrintableAndControls_RoundTripsThroughTranslator(char character)
    {
        bool found = lookup.TryGet(character, out HostKeystroke keystroke);
        byte? code = found ? Translator.Translate(keystroke.Key, keystroke.Modifiers) : null;

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True, $"No keystroke for U+{(int)character:X4}");
            Assert.That(code, Is.EqualTo((byte)character));
        });
    }

    /// <summary>
    /// Verifies that Backspace, not Left, wins for <c>$08</c> when both use no modifiers.
    /// </summary>
    [Test]
    public void TryGet_Backspace_PrefersBackspaceOverLeft()
    {
        bool found = lookup.TryGet('\b', out HostKeystroke keystroke);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(keystroke.Key, Is.EqualTo(KeyboardKey.Backspace));
            Assert.That(keystroke.Modifiers, Is.EqualTo(KeyboardModifiers.None));
        });
    }

    private static IEnumerable<char> MappedCharacters()
    {
        for (int code = 0x20; code <= 0x7E; code++)
        {
            yield return (char)code;
        }

        yield return '\r';
        yield return '\t';
        yield return '\b';
    }
}