// <copyright file="ReferenceGoldenTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Rendering.Tests;

using BadMango.Emulator.Devices;

/// <summary>
/// Verifies that the current main-branch renderer remains bit-for-bit compatible with the
/// frozen reference frames for the video-pipeline epic.
/// </summary>
[TestFixture]
public sealed class ReferenceGoldenTests
{
    /// <summary>
    /// Writes all reference frames when the regeneration switch is set.
    /// </summary>
    /// <remarks>
    /// NUnit calls this once before the fixture. A normal run leaves the files untouched.
    /// </remarks>
    [OneTimeSetUp]
    public static void EnsureReferenceGoldensAreCurrent()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(ReferenceGoldenHelper.UpdateGoldenEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var cell in ReferenceGoldenHelper.EnumerateMatrixCells())
        {
            uint[] pixels = ReferenceGoldenHelper.RenderReferenceFrame(cell.Mode, cell.IsPage2, cell.ColorMode, cell.UseAltCharSet, cell.FlashState);
            ReferenceGoldenHelper.WriteReferenceFrame(cell.Mode, cell.IsPage2, cell.ColorMode, cell.UseAltCharSet, cell.FlashState, pixels);
        }
    }

    /// <summary>
    /// Gets the full matrix of golden cells to validate.
    /// </summary>
    /// <returns>The full matrix of matrix cells to validate.</returns>
    public static IEnumerable<TestCaseData> MatrixCells()
    {
        foreach (var cell in ReferenceGoldenHelper.EnumerateMatrixCells())
        {
            yield return new TestCaseData(cell.Mode, cell.IsPage2, cell.ColorMode, cell.UseAltCharSet, cell.FlashState)
                .SetName($"{cell.Mode}_{(cell.IsPage2 ? "p2" : "p1")}_{cell.ColorMode}_{(cell.UseAltCharSet ? "altchar1" : "altchar0")}_{(cell.FlashState ? "flash1" : "flash0")}");
        }
    }

    /// <summary>
    /// Verifies the current renderer output matches the stored golden frame bit for bit.
    /// </summary>
    /// <param name="mode">The source video mode.</param>
    /// <param name="isPage2">Whether page 2 is active.</param>
    /// <param name="colorMode">The display color mode.</param>
    /// <param name="useAltCharSet">Whether the alternate character set is enabled.</param>
    /// <param name="flashState">Whether the flashing text is active.</param>
    [TestCaseSource(nameof(MatrixCells))]
    public void CurrentRendererMatchesStoredReference(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(ReferenceGoldenHelper.UpdateGoldenEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            Assert.Ignore("Reference golden regeneration is active; compare step is skipped while files are refreshed.");
        }

        var actual = ReferenceGoldenHelper.RenderReferenceFrame(mode, isPage2, colorMode, useAltCharSet, flashState);
        var expected = ReferenceGoldenHelper.LoadReferenceFrame(mode, isPage2, colorMode, useAltCharSet, flashState);

        Assert.That(actual, Is.EqualTo(expected), $"Golden mismatch for {mode}_{(isPage2 ? "p2" : "p1")}_{colorMode}_{(useAltCharSet ? "altchar1" : "altchar0")}_{(flashState ? "flash1" : "flash0")}");
    }
}