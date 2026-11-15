// <copyright file="HexBytesJsonConverter.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Serializes a byte payload as a compact uppercase hex string (for example <c>"01A9FF"</c>)
/// instead of the default base64 or a number array.
/// </summary>
/// <remarks>
/// Hex costs two characters per byte, keeps byte boundaries visible, and reads like a monitor
/// dump. Reading accepts upper- or lowercase digits and requires an even number of characters.
/// </remarks>
public sealed class HexBytesJsonConverter : JsonConverter<IReadOnlyList<byte>>
{
    /// <inheritdoc />
    public override IReadOnlyList<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a hex string for a byte payload but found {reader.TokenType}.");
        }

        string hex = reader.GetString() ?? string.Empty;
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new JsonException("Byte payload is not a valid even-length hex string.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<byte> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = value as byte[] ?? [.. value];
        writer.WriteStringValue(Convert.ToHexString(bytes));
    }
}