// <copyright file="Dos33Details.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// DOS 3.3 facts beyond the version label.
/// </summary>
public sealed record Dos33Details : IGuestOsDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Dos33Details"/> class.
    /// </summary>
    /// <param name="standardVectors">
    /// <see langword="true"/> when the five page-3 <c>JMP</c>s use the 48K targets.
    /// </param>
    public Dos33Details(bool standardVectors)
    {
        this.StandardVectors = standardVectors;
    }

    /// <summary>Gets a value indicating whether the vectors are the standard 48K targets.</summary>
    /// <value><see langword="false"/> for a DOS 3.3-compatible system with other targets.</value>
    public bool StandardVectors { get; }
}