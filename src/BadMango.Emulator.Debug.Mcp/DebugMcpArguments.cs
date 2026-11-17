// <copyright file="DebugMcpArguments.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.Globalization;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;

/// <summary>
/// Parses inspection and breakpoint tool arguments with the debug command address parser.
/// </summary>
internal static class DebugMcpArguments
{
    /// <summary>
    /// Parses an address with <see cref="AddressParser"/>.
    /// </summary>
    /// <param name="text">The address text.</param>
    /// <param name="machine">The machine used to resolve soft-switch names.</param>
    /// <param name="address">The parsed address.</param>
    /// <param name="error">The error message when parsing fails.</param>
    /// <returns><see langword="true"/> when <paramref name="address"/> is valid.</returns>
    public static bool TryParseAddress(string? text, IMachine? machine, out uint address, out string? error)
    {
        address = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "An address is required.";
            return false;
        }

        if (text.Length > 64)
        {
            error = $"Invalid address '{Trim(text)}'.";
            return false;
        }

        if (!AddressParser.TryParse(text.Trim(), machine, out address))
        {
            error = $"Invalid address '{text.Trim()}'.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks that a positive length stays inside the configured maximum and does not wrap the address space.
    /// </summary>
    /// <param name="address">The starting address.</param>
    /// <param name="length">The requested length.</param>
    /// <param name="maximum">The inclusive maximum length.</param>
    /// <param name="error">The error message when the range is rejected.</param>
    /// <returns><see langword="true"/> when the range is allowed.</returns>
    public static bool TryValidateRange(uint address, int length, int maximum, out string? error)
    {
        error = null;
        if (length < 1 || length > maximum)
        {
            error = $"Length must be from 1 through {maximum}.";
            return false;
        }

        if (address > uint.MaxValue - (uint)(length - 1))
        {
            error = "The address range wraps past the end of the address space.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parses packed hex or space-separated bytes.
    /// </summary>
    /// <param name="text">The byte text.</param>
    /// <param name="data">The parsed bytes.</param>
    /// <param name="error">The error message when parsing fails.</param>
    /// <returns><see langword="true"/> when <paramref name="data"/> is valid.</returns>
    public static bool TryParseBytes(string? text, out byte[] data, out string? error)
    {
        data = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "At least one byte is required.";
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Length > (DebugMcpLimits.MaxWriteLength * 5))
        {
            error = $"Write length exceeds maximum ({DebugMcpLimits.MaxWriteLength} bytes).";
            return false;
        }

        if (trimmed.IndexOfAny([' ', '\t', ',']) >= 0)
        {
            return TryParseSeparatedBytes(trimmed, out data, out error);
        }

        return TryParsePackedBytes(trimmed, out data, out error);
    }

    /// <summary>
    /// Parses a watch access name. Accepts Read, Write, ReadWrite, and the r, w, and rw command tokens.
    /// </summary>
    /// <param name="text">The access text.</param>
    /// <param name="access">The parsed access.</param>
    /// <param name="error">The error message when parsing fails.</param>
    /// <returns><see langword="true"/> when <paramref name="access"/> is valid.</returns>
    public static bool TryParseWatchAccess(string? text, out WatchAccess access, out string? error)
    {
        access = WatchAccess.ReadWrite;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Watch access is required. Use Read, Write, or ReadWrite.";
            return false;
        }

        if (text.Length > 32)
        {
            error = $"Unknown watch access '{Trim(text)}'. Use Read, Write, or ReadWrite.";
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "r":
            case "read":
                access = WatchAccess.Read;
                return true;
            case "w":
            case "write":
                access = WatchAccess.Write;
                return true;
            case "rw":
            case "readwrite":
                access = WatchAccess.ReadWrite;
                return true;
            default:
                error = $"Unknown watch access '{text.Trim()}'. Use Read, Write, or ReadWrite.";
                return false;
        }
    }

    /// <summary>
    /// Accepts a missing label and rejects one that is longer than <see cref="DebugMcpLimits.MaxLabelLength"/>.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="normalized">The label to store, or <see langword="null"/> when omitted.</param>
    /// <param name="error">The error message when the label is too long.</param>
    /// <returns><see langword="true"/> when the label is allowed.</returns>
    public static bool TryNormalizeLabel(string? label, out string? normalized, out string? error)
    {
        normalized = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        error = null;
        if (normalized is not null && normalized.Length > DebugMcpLimits.MaxLabelLength)
        {
            error = $"Label exceeds maximum ({DebugMcpLimits.MaxLabelLength} characters).";
            normalized = null;
            return false;
        }

        return true;
    }

    private static bool TryParseSeparatedBytes(string text, out byte[] data, out string? error)
    {
        data = [];
        string[] tokens = text.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "At least one byte is required.";
            return false;
        }

        if (tokens.Length > DebugMcpLimits.MaxWriteLength)
        {
            error = $"Write length exceeds maximum ({DebugMcpLimits.MaxWriteLength} bytes).";
            return false;
        }

        var bytes = new byte[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!TryParseByte(tokens[i], out bytes[i]))
            {
                error = $"Invalid byte value '{tokens[i]}'.";
                return false;
            }
        }

        data = bytes;
        error = null;
        return true;
    }

    private static bool TryParsePackedBytes(string text, out byte[] data, out string? error)
    {
        data = [];
        string packed = text;
        if (packed.StartsWith('$'))
        {
            packed = packed[1..];
        }
        else if (packed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            packed = packed[2..];
        }

        if (packed.Length == 0 || (packed.Length % 2) != 0)
        {
            error = "Byte data must be an even-length hex string or space-separated bytes.";
            return false;
        }

        if (packed.Length / 2 > DebugMcpLimits.MaxWriteLength)
        {
            error = $"Write length exceeds maximum ({DebugMcpLimits.MaxWriteLength} bytes).";
            return false;
        }

        try
        {
            data = Convert.FromHexString(packed);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            error = "Byte data is not valid hexadecimal.";
            return false;
        }
    }

    private static bool TryParseByte(string token, out byte value)
    {
        value = 0;
        string text = token;
        if (text.StartsWith('$'))
        {
            text = text[1..];
        }
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length is 0 or > 2)
        {
            return false;
        }

        return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string Trim(string text)
    {
        const int Limit = 32;
        string trimmed = text.Trim();
        return trimmed.Length <= Limit ? trimmed : trimmed[..Limit];
    }
}