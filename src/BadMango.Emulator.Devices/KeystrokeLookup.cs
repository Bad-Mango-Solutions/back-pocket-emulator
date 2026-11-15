// <copyright file="KeystrokeLookup.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Numerics;

/// <summary>
/// Immutable reverse map from a character to the <see cref="HostKeystroke"/> that produces it.
/// </summary>
/// <remarks>
/// Built by enumerating every <see cref="KeyboardKey"/> with no modifiers, Shift, Control, and
/// Control+Shift. When several keystrokes produce the same character, the one with the fewest
/// modifiers wins. Equal modifier counts keep the earliest key in <see cref="KeyboardKey"/>
/// declaration order, so Backspace wins over Left for <c>$08</c> and Return wins over a Control
/// chord for <c>$0D</c>. Keys whose translation is <see langword="null"/> are skipped. The table
/// never changes, so one instance may be shared.
/// </remarks>
public sealed class KeystrokeLookup
{
    private static readonly KeyboardModifiers[] ModifierSets =
    [
        KeyboardModifiers.None,
        KeyboardModifiers.Shift,
        KeyboardModifiers.Control,
        KeyboardModifiers.Control | KeyboardModifiers.Shift,
    ];

    private readonly Dictionary<char, HostKeystroke> byCharacter;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeystrokeLookup"/> class from a translator.
    /// </summary>
    /// <param name="translator">The translator that defines which keystrokes produce a code.</param>
    /// <exception cref="ArgumentNullException"><paramref name="translator"/> is <see langword="null"/>.</exception>
    public KeystrokeLookup(IKeyTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(translator);

        var map = new Dictionary<char, HostKeystroke>();
        foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
        {
            foreach (KeyboardModifiers modifiers in ModifierSets)
            {
                byte? code = translator.Translate(key, modifiers);
                if (code is null)
                {
                    continue;
                }

                char character = (char)code.Value;
                var candidate = new HostKeystroke(key, modifiers);
                if (!map.TryGetValue(character, out HostKeystroke existing)
                    || ModifierCount(modifiers) < ModifierCount(existing.Modifiers))
                {
                    map[character] = candidate;
                }
            }
        }

        byCharacter = map;
    }

    /// <summary>
    /// Looks up the keystroke that produces <paramref name="character"/>.
    /// </summary>
    /// <param name="character">The character to produce. The low 7 bits are the machine code.</param>
    /// <param name="keystroke">The winning keystroke when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a keystroke produces <paramref name="character"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(char character, out HostKeystroke keystroke) => byCharacter.TryGetValue(character, out keystroke);

    private static int ModifierCount(KeyboardModifiers modifiers) => BitOperations.PopCount((uint)modifiers);
}