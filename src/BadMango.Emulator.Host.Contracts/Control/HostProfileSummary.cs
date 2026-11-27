// <copyright file="HostProfileSummary.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A profile id and name, without the document body.
/// </summary>
/// <param name="Id">The profile id.</param>
/// <param name="Name">The template name.</param>
public sealed record HostProfileSummary(string Id, string Name);