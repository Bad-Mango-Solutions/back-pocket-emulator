// <copyright file="IAuxiliaryLanguageCard.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Selects which language-card RAM bank is on the bus.
/// </summary>
/// <remarks>
/// <para>
/// On an Apple IIe, ALTZP ($C008/$C009) switches zero page, the stack, and the
/// language-card RAM together. The $C080-$C08F switches still choose ROM or RAM
/// and which $D000 bank, and those switches apply to both the main and auxiliary
/// 16K. This interface does not place that 16K inside the extended 80-column
/// card's 64K array. The language card owns a separate backing store and
/// publishes it through its swap groups.
/// </para>
/// <para>
/// The implementation republishes the page table when the selection changes.
/// Individual memory reads do not ask this interface which bank is current.
/// </para>
/// </remarks>
public interface IAuxiliaryLanguageCard
{
    /// <summary>
    /// Selects the auxiliary or main language-card RAM.
    /// </summary>
    /// <param name="selected">
    /// <see langword="true"/> to map the auxiliary 16K at $D000-$FFFF;
    /// <see langword="false"/> to map the main 16K.
    /// </param>
    void SetAuxiliaryBank(bool selected);
}