// <copyright file="IGuestOsDetails.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Optional facts that belong to one operating system.
/// </summary>
/// <remarks>
/// <see cref="GuestOsInfo"/> stays the same for every OS. A probe that has more than a
/// version label returns its own type, such as <see cref="ProDos8Details"/>. Callers that
/// know the id cast <see cref="GuestOsInfo.Details"/>. Other callers ignore it.
/// </remarks>
public interface IGuestOsDetails
{
}