// <copyright file="Cpu65C02HardwareVectorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using SingleStep;

/// <summary>
/// Checks <see cref="BadMango.Emulator.Emulation.Cpu.Cpu65C02"/> against the SingleStepTests
/// <c>wdc65c02</c> hardware vectors: final registers, flags, RAM, and the total cycle count.
/// </summary>
/// <remarks>
/// <para>
/// The fast subset is every 50th case of each opcode file (200 cases, or none for an opcode the
/// upstream set leaves empty) from commit <c>2f6980a2d95757486c7bee24355c360e40e2a224</c>.
/// That snapshot is MIT licensed, Copyright (c) 2024 Thomas Harte et al. The notice is in
/// <c>Vectors/SingleStepTests-65x02-LICENSE.txt</c>. <c>$CB</c> WAI and <c>$DB</c> STP are empty
/// upstream because those instructions do not fit a single-step test.
/// </para>
/// <para>
/// The full 10,000-case files are not committed. Category <c>SingleStepFull</c> downloads them
/// into the local cache, or reads <c>WDC65C02_VECTOR_DIR</c> when that variable is set.
/// Per-cycle bus traces are not compared.
/// </para>
/// </remarks>
[TestFixture]
public sealed class Cpu65C02HardwareVectorTests
{
    /// <summary>NUnit category for the 10,000-case run. CI selects it on its own.</summary>
    public const string FullCategory = "SingleStepFull";

    private static readonly Lazy<Wdc65C02VectorSet> LoadedSubset = new(Wdc65C02VectorSet.LoadEmbedded);

    /// <summary>
    /// The embedded subset has every opcode, the pinned commit, and the fixed stride.
    /// </summary>
    [Test]
    public void Subset_CoversEveryOpcodeFromThePinnedCommit()
    {
        Wdc65C02VectorSet subset = LoadedSubset.Value;
        Assert.That(subset.SampleStride, Is.EqualTo(Wdc65C02VectorSet.Stride));
        for (int opcode = 0; opcode < 256; opcode++)
        {
            int expected = Wdc65C02VectorSet.IsOmitted((byte)opcode) ? 0 : Wdc65C02VectorSet.CasesPerOpcode;
            Assert.That(subset.CasesFor((byte)opcode), Has.Length.EqualTo(expected), $"opcode ${opcode:X2}");
        }
    }

    /// <summary>
    /// One opcode of the committed subset matches final state and total cycle count.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    [TestCaseSource(nameof(FastOpcodes))]
    public void Opcode_MatchesFinalStateAndCycleCount(byte opcode)
    {
        Wdc65C02VectorCase[] cases = LoadedSubset.Value.CasesFor(opcode);
        int expected = Wdc65C02VectorSet.IsOmitted(opcode) ? 0 : Wdc65C02VectorSet.CasesPerOpcode;
        var runner = new Wdc65C02VectorRunner();
        Wdc65C02VectorRunResult result = runner.ExecuteAll(opcode, cases);
        Assert.Multiple(() =>
        {
            Assert.That(result.Cases, Is.EqualTo(expected), $"opcode ${opcode:X2} case count");
            Assert.That(result.Failed, Is.Zero, result.Example);
        });
    }

    /// <summary>
    /// One opcode of the full upstream file matches final state and total cycle count.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <returns>A task that completes when the opcode file has been checked.</returns>
    [TestCaseSource(nameof(FullOpcodes))]
    [Category(FullCategory)]
    [Explicit("Full 10,000-case SingleStepTests run. CI selects category SingleStepFull.")]
    public async Task Opcode_MatchesFullHardwareSet(byte opcode)
    {
        string path = await Wdc65C02VectorCatalog.EnsureAsync(opcode, TestContext.CurrentContext.CancellationToken)
            .ConfigureAwait(true);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var runner = new Wdc65C02VectorRunner();
        Wdc65C02VectorRunResult result = runner.ExecuteJson(opcode, stream);
        int expected = Wdc65C02VectorSet.IsOmitted(opcode) ? 0 : Wdc65C02VectorSet.FullCasesPerOpcode;
        Assert.Multiple(() =>
        {
            Assert.That(result.Cases, Is.EqualTo(expected), $"opcode ${opcode:X2} case count");
            Assert.That(result.Failed, Is.Zero, result.Example);
        });
    }

    private static IEnumerable<TestCaseData> FastOpcodes() => Enumerate("MatchesFinalStateAndCycleCount");

    private static IEnumerable<TestCaseData> FullOpcodes() => Enumerate("MatchesFullHardwareSet");

    private static IEnumerable<TestCaseData> Enumerate(string suffix)
    {
        for (int opcode = 0; opcode < 256; opcode++)
        {
            var data = new TestCaseData((byte)opcode);
            data.SetName($"Opcode_{opcode:X2}_{suffix}");
            if (TryIgnore((byte)opcode, out string? reason))
            {
                data.Ignore(reason);
            }

            yield return data;
        }
    }

    private static bool TryIgnore(byte opcode, out string reason)
    {
        switch (opcode)
        {
            case 0x28:
                reason = "Tracked by #570";
                return true;
            case 0x40:
                reason = "Tracked by #571";
                return true;
            case 0x5C:
                reason = "Tracked by #572";
                return true;
            case 0xE1:
                reason = "Tracked by #573";
                return true;
            case 0xE5:
                reason = "Tracked by #574";
                return true;
            case 0xE9:
                reason = "Tracked by #575";
                return true;
            case 0xED:
                reason = "Tracked by #576";
                return true;
            case 0xF1:
                reason = "Tracked by #577";
                return true;
            case 0xF2:
                reason = "Tracked by #578";
                return true;
            case 0xF5:
                reason = "Tracked by #579";
                return true;
            case 0xF9:
                reason = "Tracked by #580";
                return true;
            case 0xFD:
                reason = "Tracked by #581";
                return true;
            default:
                reason = string.Empty;
                return false;
        }
    }
}