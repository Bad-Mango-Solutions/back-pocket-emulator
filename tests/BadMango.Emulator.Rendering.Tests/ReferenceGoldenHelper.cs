// <copyright file="ReferenceGoldenHelper.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Rendering.Tests;

using System.Buffers.Binary;
using System.IO.Compression;

using BadMango.Emulator.Devices;

/// <summary>
/// Provides helpers for loading and writing frozen reference frames for the video pipeline golden set.
/// </summary>
public static class ReferenceGoldenHelper
{
    /// <summary>
    /// Environment variable that allows the goldens to be refreshed.
    /// </summary>
    public const string UpdateGoldenEnvironmentVariable = "BPE_UPDATE_REFERENCE_GOLDENS";

    private const int CanonicalWidth = 560;
    private const int CanonicalHeight = 384;
    private const int BytesPerPixel = 4;
    private const int PixelCount = CanonicalWidth * CanonicalHeight;

    /// <summary>
    /// Gets the full path to the frozen reference frame for a matrix cell.
    /// </summary>
    /// <param name="mode">The video mode.</param>
    /// <param name="isPage2">Whether page 2 is active.</param>
    /// <param name="colorMode">The target display color mode.</param>
    /// <param name="useAltCharSet">Whether the alternate character set is enabled.</param>
    /// <param name="flashState">Whether flashing text is currently active.</param>
    /// <returns>The path to the golden gzip file.</returns>
    public static string GetGoldenPath(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState)
    {
        string fileName = GetGoldenFileName(mode, isPage2, colorMode, useAltCharSet, flashState);
        string directory = GetGoldenDirectory();
        return Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Loads a reference frame from the frozen set for the given matrix cell.
    /// Each pixel is a little-endian BGRA word.
    /// </summary>
    /// <param name="mode">The video mode.</param>
    /// <param name="isPage2">Whether page 2 is active.</param>
    /// <param name="colorMode">The target display color mode.</param>
    /// <param name="useAltCharSet">Whether the alternate character set is enabled.</param>
    /// <param name="flashState">Whether flashing text is currently active.</param>
    /// <returns>The pixel buffer for the stored reference frame.</returns>
    public static uint[] LoadReferenceFrame(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState)
    {
        string filePath = GetGoldenPath(mode, isPage2, colorMode, useAltCharSet, flashState);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Reference golden not found: {filePath}", filePath);
        }

        using var input = File.OpenRead(filePath);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        gzip.CopyTo(memoryStream);

        byte[] rawPixels = memoryStream.ToArray();
        if (rawPixels.Length != PixelCount * BytesPerPixel)
        {
            throw new InvalidOperationException(
                $"Reference frame '{filePath}' has {rawPixels.Length} bytes but expected {PixelCount * BytesPerPixel} bytes.");
        }

        var pixels = new uint[PixelCount];
        for (int i = 0; i < PixelCount; i++)
        {
            pixels[i] = BinaryPrimitives.ReadUInt32LittleEndian(rawPixels.AsSpan(i * BytesPerPixel, BytesPerPixel));
        }

        return pixels;
    }

    /// <summary>
    /// Writes a rendered frame to the golden output path.
    /// Each pixel is stored as a little-endian BGRA word.
    /// </summary>
    /// <param name="mode">The video mode.</param>
    /// <param name="isPage2">Whether page 2 is active.</param>
    /// <param name="colorMode">The target display color mode.</param>
    /// <param name="useAltCharSet">Whether the alternate character set is enabled.</param>
    /// <param name="flashState">Whether flashing text is currently active.</param>
    /// <param name="pixels">The rendered pixels to store.</param>
    public static void WriteReferenceFrame(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState,
        ReadOnlySpan<uint> pixels)
    {
        string filePath = GetGoldenPath(mode, isPage2, colorMode, useAltCharSet, flashState);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] rawPixels = new byte[pixels.Length * BytesPerPixel];
        for (int i = 0; i < pixels.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(rawPixels.AsSpan(i * BytesPerPixel, BytesPerPixel), pixels[i]);
        }

        using var outStream = File.Create(filePath);
        using var gzip = new GZipStream(outStream, CompressionLevel.Optimal, leaveOpen: true);
        gzip.Write(rawPixels, 0, rawPixels.Length);
        gzip.Flush();
    }

    /// <summary>
    /// Renders a frame for the given cell using the current renderer and the deterministic reference memory fixture.
    /// </summary>
    /// <param name="mode">The video mode.</param>
    /// <param name="isPage2">Whether page 2 is active.</param>
    /// <param name="colorMode">The target display color mode.</param>
    /// <param name="useAltCharSet">Whether the alternate character set is enabled.</param>
    /// <param name="flashState">Whether flashing text is currently active.</param>
    /// <returns>The rendered frame as a BGRA pixel buffer.</returns>
    public static uint[] RenderReferenceFrame(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState)
    {
        (byte[] mainMemory, byte[] auxMemory) = CreateReferenceVideoMemory(isPage2);
        var renderer = new Pocket2VideoRenderer();
        var pixels = new uint[PixelCount];
        renderer.Clear(pixels);

        if (mode == VideoMode.Text80)
        {
            renderer.RenderFrame(
                pixels,
                mode,
                address => mainMemory[address],
                DefaultCharacterRom.GetRomData(),
                useAltCharSet,
                isPage2,
                flashState,
                noFlash1Enabled: false,
                noFlash2Enabled: false,
                colorMode,
                address => auxMemory[address]);

            return pixels;
        }

        renderer.RenderFrame(
            pixels,
            mode,
            address => mainMemory[address],
            DefaultCharacterRom.GetRomData(),
            useAltCharSet,
            isPage2,
            flashState,
            noFlash1Enabled: false,
            noFlash2Enabled: false,
            colorMode,
            null);

        return pixels;
    }

    /// <summary>
    /// Enumerates the frozen reference matrix cells used for the golden set.
    /// </summary>
    /// <returns>An enumeration of matrix cell arguments.</returns>
    public static IEnumerable<(VideoMode Mode, bool IsPage2, DisplayColorMode ColorMode, bool UseAltCharSet, bool FlashState)> EnumerateMatrixCells()
    {
        var modes = new[]
        {
            VideoMode.Text40,
            VideoMode.Text80,
            VideoMode.LoRes,
            VideoMode.DoubleLoRes,
            VideoMode.HiRes,
            VideoMode.DoubleHiRes,
            VideoMode.LoResMixed,
            VideoMode.DoubleLoResMixed,
            VideoMode.HiResMixed,
            VideoMode.DoubleHiResMixed,
        };

        foreach (var mode in modes)
        {
            bool includesTextMode = mode is VideoMode.Text40 or VideoMode.Text80 or VideoMode.LoResMixed or VideoMode.DoubleLoResMixed or VideoMode.HiResMixed or VideoMode.DoubleHiResMixed;

            foreach (bool isPage2 in new[] { false, true })
            {
                foreach (DisplayColorMode colorMode in Enum.GetValues<DisplayColorMode>())
                {
                    if (includesTextMode)
                    {
                        foreach (bool useAltCharSet in new[] { false, true })
                        {
                            foreach (bool flashState in new[] { false, true })
                            {
                                yield return (mode, isPage2, colorMode, useAltCharSet, flashState);
                            }
                        }
                    }
                    else
                    {
                        yield return (mode, isPage2, colorMode, false, false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates the deterministic main and auxiliary video RAM shared by the reference goldens.
    /// </summary>
    /// <param name="isPage2">Whether to bias the pattern for page 2.</param>
    /// <returns>The main and auxiliary memories for the current frame.</returns>
    /// <remarks>
    /// The pattern covers every display byte the legacy renderer reads. Step 6's parity test
    /// passes this same fixture to the new scanner so both paths see identical video RAM.
    /// </remarks>
    public static (byte[] MainMemory, byte[] AuxMemory) CreateReferenceVideoMemory(bool isPage2)
    {
        var mainMemory = new byte[ushort.MaxValue + 1];
        var auxMemory = new byte[ushort.MaxValue + 1];

        for (int address = 0x0400; address <= 0x5FFF; address++)
        {
            int pageOffset = address - 0x0400;
            int basePattern = ((pageOffset * 17) + (isPage2 ? 0x5A : 0x2A) + (address >> 2)) & 0xFF;

            byte mainValue = (byte)basePattern;
            byte auxValue = (byte)(mainValue ^ 0x55);

            if (address >= 0x0400 && address < 0x0800)
            {
                int row = (address - 0x0400) / 128;
                int col = (address - 0x0400) % 128;
                mainValue = (byte)(((row * 29) + (col * 13) + (isPage2 ? 0x5A : 0x10)) & 0xFF);
                auxValue = (byte)((mainValue + 0x3C) & 0xFF);
            }
            else if (address >= 0x0800 && address < 0x0C00)
            {
                int row = (address - 0x0800) / 128;
                int col = (address - 0x0800) % 128;
                mainValue = (byte)(((row * 19) + (col * 7) + (isPage2 ? 0xA5 : 0x3C)) & 0xFF);
                auxValue = (byte)((mainValue ^ 0xA5) & 0xFF);
            }
            else if (address >= 0x2000 && address <= 0x3FFF)
            {
                int scanline = (address - 0x2000) >> 6;
                int byteCol = (address - 0x2000) & 0x3F;
                byte hiResPattern = (byte)(((byteCol * 9) ^ (scanline * 13) ^ (isPage2 ? 0xAA : 0x55)) & 0x7F);
                hiResPattern |= (byte)(((byteCol ^ scanline) & 1) == 0 ? 0x80 : 0x00);

                if ((byteCol & 3) == 0)
                {
                    hiResPattern |= 0x03;
                }

                mainValue = hiResPattern;
                auxValue = (byte)(~hiResPattern & 0xFF);
            }
            else if (address >= 0x4000 && address <= 0x5FFF)
            {
                int scanline = (address - 0x4000) >> 6;
                int byteCol = (address - 0x4000) & 0x3F;
                byte hiResPattern = (byte)(((byteCol * 11) ^ (scanline * 17) ^ (isPage2 ? 0x55 : 0xAA)) & 0x7F);
                hiResPattern |= (byte)(((byteCol + scanline) & 1) == 0 ? 0x00 : 0x80);

                if ((byteCol & 5) == 1)
                {
                    hiResPattern |= 0x40;
                }

                if ((byteCol & 7) == 2)
                {
                    hiResPattern &= unchecked((byte)~0x20);
                }

                mainValue = hiResPattern;
                auxValue = (byte)(hiResPattern ^ 0x3C);
            }

            mainMemory[address] = mainValue;
            auxMemory[address] = auxValue;
        }

        return (mainMemory, auxMemory);
    }

    private static string GetGoldenFileName(
        VideoMode mode,
        bool isPage2,
        DisplayColorMode colorMode,
        bool useAltCharSet,
        bool flashState)
    {
        string page = isPage2 ? "p2" : "p1";
        string alt = useAltCharSet ? "altchar1" : "altchar0";
        string flash = flashState ? "flash1" : "flash0";
        return $"{mode}_{page}_{colorMode}_{alt}_{flash}.bgra.gz";
    }

    private static string GetGoldenDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string outputGoldenDirectory = Path.Combine(baseDirectory, "Goldens", "Reference");
        if (Directory.Exists(outputGoldenDirectory))
        {
            return outputGoldenDirectory;
        }

        string currentDirectory = Environment.CurrentDirectory;
        string localGoldenDirectory = Path.Combine(currentDirectory, "tests", "BadMango.Emulator.Rendering.Tests", "Goldens", "Reference");
        if (Directory.Exists(localGoldenDirectory))
        {
            return localGoldenDirectory;
        }

        string repositoryGoldenDirectory = Path.Combine(currentDirectory, "Goldens", "Reference");
        if (Directory.Exists(repositoryGoldenDirectory))
        {
            return repositoryGoldenDirectory;
        }

        return outputGoldenDirectory;
    }
}