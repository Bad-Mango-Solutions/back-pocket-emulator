// <copyright file="PowerOnMemoryPattern.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Identifies a memory pattern applied when the machine is powered on.
/// </summary>
public enum PowerOnMemoryPatternKind
{
    /// <summary>
    /// All memory bytes are zero.
    /// </summary>
    Zero,

    /// <summary>
    /// Apple IIe-compatible stripes with seeded noise.
    /// </summary>
    AppleIIe,

    /// <summary>
    /// Alternating zero and FF bytes.
    /// </summary>
    Alternating00FF,

    /// <summary>
    /// Fully random memory bytes.
    /// </summary>
    Random,
}

/// <summary>
/// Describes a deterministic power-on memory pattern.
/// </summary>
/// <param name="Kind">The pattern kind.</param>
/// <param name="Seed">The seed used to generate seeded noise.</param>
public sealed record PowerOnMemoryPattern(PowerOnMemoryPatternKind Kind, int Seed)
{
    /// <summary>
    /// Gets the default Apple IIe-compatible power-on pattern.
    /// </summary>
    public static PowerOnMemoryPattern Default { get; } =
        new(PowerOnMemoryPatternKind.AppleIIe, 0);

    /// <summary>
    /// Applies this pattern to the supplied byte span.
    /// </summary>
    /// <param name="destination">The memory span to update.</param>
    public void Apply(Span<byte> destination)
    {
        switch (Kind)
        {
            case PowerOnMemoryPatternKind.Zero:
                destination.Clear();
                break;
            case PowerOnMemoryPatternKind.Alternating00FF:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = (i & 1) == 0 ? (byte)0x00 : (byte)0xFF;
                }

                break;
            case PowerOnMemoryPatternKind.Random:
                var random = new Random(Seed);
                random.NextBytes(destination);
                break;
            case PowerOnMemoryPatternKind.AppleIIe:
                ApplyAppleIIe(destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown power-on memory pattern.");
        }
    }

    private void ApplyAppleIIe(Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (i & 3) is 0 or 1 ? (byte)0xFF : (byte)0x00;
        }

        var random = new Random(Seed);
        for (int block = 0; block < destination.Length; block += 512)
        {
            int limit = Math.Min(block + 512, destination.Length);
            foreach (int offset in new[] { 0x28, 0x29, 0x68, 0x69 })
            {
                int index = block + offset;
                if (index < limit)
                {
                    destination[index] = (byte)random.Next(256);
                }
            }
        }

        if (destination.Length > 0x4F)
        {
            destination[0x4E] = (byte)(0x20 | random.Next(0x20));
            destination[0x4F] = (byte)(0x20 | random.Next(0x20));
        }

        if (destination.Length > 0x620B)
        {
            destination[0x620B] = 0;
        }

        if (destination.Length > 0xBFFF)
        {
            destination[0xBFFD] = 0;
            destination[0xBFFE] = 0;
            destination[0xBFFF] = 0;
        }
    }
}