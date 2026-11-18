// <copyright file="Wdc65C02VectorCatalog.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

/// <summary>
/// Locates the full upstream <c>wdc65c02/v1</c> opcode files, downloading them into a cache
/// when <c>WDC65C02_VECTOR_DIR</c> is not set.
/// </summary>
internal static class Wdc65C02VectorCatalog
{
    private const string RawRoot = "https://raw.githubusercontent.com/SingleStepTests/65x02/"
        + Wdc65C02VectorSet.UpstreamCommit
        + "/wdc65c02/v1/";

    /// <summary>
    /// Returns a readable path for one opcode file, from the override directory or the local cache.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The path of <c>oo.json</c> for <paramref name="opcode"/>.</returns>
    internal static async Task<string> EnsureAsync(byte opcode, CancellationToken cancellationToken)
    {
        string directory = ResolveDirectory(out bool downloadMissing);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{opcode:x2}.json");
        if (IsUsable(opcode, path))
        {
            return path;
        }

        if (!downloadMissing)
        {
            throw new FileNotFoundException(
                $"WDC65C02_VECTOR_DIR has no file for opcode ${opcode:X2}.",
                path);
        }

        string url = RawRoot + $"{opcode:x2}.json";
        string partial = path + ".partial";
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        client.DefaultRequestHeaders.Add("User-Agent", "back-pocket-emulator-tests/1.0");

        Exception? last = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                File.Move(partial, path, overwrite: true);
                if (!IsUsable(opcode, path))
                {
                    throw new InvalidDataException($"Downloaded opcode ${opcode:X2} from {url} is empty.");
                }

                return path;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                last = ex;
            }
        }

        throw new InvalidOperationException($"Failed to download opcode ${opcode:X2} from {url}.", last);
    }

    private static bool IsUsable(byte opcode, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Length > 0 || Wdc65C02VectorSet.IsOmitted(opcode);
    }

    private static string ResolveDirectory(out bool downloadMissing)
    {
        string? overrideDir = Environment.GetEnvironmentVariable("WDC65C02_VECTOR_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            downloadMissing = false;
            return overrideDir;
        }

        downloadMissing = true;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "back-pocket-emulator",
            "singlestep",
            "65x02",
            Wdc65C02VectorSet.UpstreamCommit);
    }
}