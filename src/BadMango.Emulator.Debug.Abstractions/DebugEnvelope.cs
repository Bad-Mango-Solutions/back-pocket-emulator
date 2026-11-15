// <copyright file="DebugEnvelope.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json.Serialization;

/// <summary>
/// An envelope used to wrap debug payloads in a versioned JSON schema.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <param name="Payload">The wrapped payload.</param>
/// <param name="Timestamp">The time the envelope was generated.</param>
/// <param name="CorrelationId">An optional correlation identifier.</param>
public sealed record DebugEnvelope<T>(
    T Payload,
    DateTimeOffset Timestamp,
    string? CorrelationId = null)
{
    /// <summary>
    /// The versioned envelope schema for emudbg payloads.
    /// </summary>
    public const string SchemaValue = "emudbg/v1";

    /// <summary>
    /// Gets the schema identifier. It is always <see cref="SchemaValue"/> and cannot be set by callers.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public string Schema => SchemaValue;

    /// <summary>
    /// Creates an envelope stamped with the current UTC time.
    /// </summary>
    /// <param name="payload">The wrapped payload.</param>
    /// <param name="correlationId">An optional correlation identifier.</param>
    /// <returns>A new envelope.</returns>
    /// <remarks>
    /// This is a factory rather than a second constructor so the record keeps a single
    /// constructor and stays deserializable by System.Text.Json.
    /// </remarks>
    public static DebugEnvelope<T> Create(T payload, string? correlationId = null)
        => new(payload, DateTimeOffset.UtcNow, correlationId);
}