// <copyright file="HostProfileDocument.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A machine profile document stored by the host. The JSON is the schema document; this contract does not validate fields.
/// </summary>
/// <param name="Id">The profile id.</param>
/// <param name="Name">The template name shown in the editor.</param>
/// <param name="Json">The profile document. Opaque to the loopback.</param>
/// <remarks>
/// The stub stores the JSON and does not validate <c>schemas/machine-profile.schema.json</c>.
/// The in-process host validates the merged document at power-on. Applying a changed template
/// to a running instance waits until the next power-on. That apply step is not part of the loopback.
/// </remarks>
public sealed record HostProfileDocument(string Id, string Name, string Json);