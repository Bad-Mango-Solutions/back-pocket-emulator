// <copyright file="VideoPipelinePairingTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Compiles pairing snippets against the built assemblies and checks CS1503 (§14.3).
/// The catalog half of §14.3 waits for the real providers.
/// </summary>
[TestFixture]
public class VideoPipelinePairingTests
{
    private const string RgbProviderOnCompositeSource = """
        using BadMango.Emulator.Devices.Tests;

        public static class Probe
        {
            public static void Run()
            {
                var composite = new CompositeStubOutput();
                composite.TryAcquireLatestFrame(new RgbStubFrameProvider(), out _);
            }
        }
        """;

    private const string CompositeProviderOnRgbSource = """
        using BadMango.Emulator.Devices.Tests;

        public static class Probe
        {
            public static void Run()
            {
                var rgb = new RgbStubOutput();
                rgb.TryAcquireLatestFrame(new CompositeStubFrameProvider(), out _);
            }
        }
        """;

    private const string CompositeProviderOnCompositeSource = """
        using BadMango.Emulator.Devices.Tests;

        public static class Probe
        {
            public static void Run()
            {
                var composite = new CompositeStubOutput();
                composite.TryAcquireLatestFrame(new CompositeStubFrameProvider(), out _);
            }
        }
        """;

    private const string RgbProviderOnRgbSource = """
        using BadMango.Emulator.Devices.Tests;

        public static class Probe
        {
            public static void Run()
            {
                var rgb = new RgbStubOutput();
                rgb.TryAcquireLatestFrame(new RgbStubFrameProvider(), out _);
            }
        }
        """;

    private static readonly MetadataReference[] References = CreateReferences();

    /// <summary>
    /// An RGB provider cannot be passed to a composite source.
    /// </summary>
    [Test]
    public void RgbProvider_OnCompositeSource_FailsWithCs1503()
    {
        AssertCs1503(RgbProviderOnCompositeSource);
    }

    /// <summary>
    /// A composite provider cannot be passed to an RGB source.
    /// </summary>
    [Test]
    public void CompositeProvider_OnRgbSource_FailsWithCs1503()
    {
        AssertCs1503(CompositeProviderOnRgbSource);
    }

    /// <summary>
    /// A composite provider compiles against a composite source.
    /// </summary>
    [Test]
    public void CompositeProvider_OnCompositeSource_Compiles()
    {
        AssertCompiles(CompositeProviderOnCompositeSource);
    }

    /// <summary>
    /// An RGB provider compiles against an RGB source.
    /// </summary>
    [Test]
    public void RgbProvider_OnRgbSource_Compiles()
    {
        AssertCompiles(RgbProviderOnRgbSource);
    }

    private static void AssertCs1503(string source)
    {
        Diagnostic[] errors = Compile(source);
        Assert.That(
            errors.Select(diagnostic => diagnostic.Id).ToArray(),
            Is.EqualTo(new[] { "CS1503" }),
            Format(errors));
    }

    private static void AssertCompiles(string source)
    {
        Diagnostic[] errors = Compile(source);
        Assert.That(errors, Is.Empty, Format(errors));
    }

    private static Diagnostic[] Compile(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "VideoPipelinePairingProbe",
            [tree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var pe = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(pe);
        return result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }

    private static MetadataReference[] CreateReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                if (path.Length > 0)
                {
                    paths.Add(path);
                }
            }
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                paths.Add(assembly.Location);
            }
        }

        return paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static string Format(Diagnostic[] errors)
    {
        return string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString()));
    }
}