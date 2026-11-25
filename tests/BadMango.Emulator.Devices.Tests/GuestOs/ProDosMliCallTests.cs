// <copyright file="ProDosMliCallTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// The shared MLI decoder: command byte, parameter pointer, names, and pathnames.
/// </summary>
[TestFixture]
public class ProDosMliCallTests
{
    /// <summary>Every TechRef command has one name, and an unused number is <c>$xx (unknown)</c>.</summary>
    [Test]
    public void Names_MatchTheTechRef_AndUnknownIsHex()
    {
        (byte Code, string Name)[] known =
        [
            (0x40, "ALLOC_INTERRUPT"),
            (0x41, "DEALLOC_INTERRUPT"),
            (0x65, "QUIT"),
            (0x80, "READ_BLOCK"),
            (0x81, "WRITE_BLOCK"),
            (0x82, "GET_TIME"),
            (0xC0, "CREATE"),
            (0xC1, "DESTROY"),
            (0xC2, "RENAME"),
            (0xC3, "SET_FILE_INFO"),
            (0xC4, "GET_FILE_INFO"),
            (0xC5, "ON_LINE"),
            (0xC6, "SET_PREFIX"),
            (0xC7, "GET_PREFIX"),
            (0xC8, "OPEN"),
            (0xC9, "NEWLINE"),
            (0xCA, "READ"),
            (0xCB, "WRITE"),
            (0xCC, "CLOSE"),
            (0xCD, "FLUSH"),
            (0xCE, "SET_MARK"),
            (0xCF, "GET_MARK"),
            (0xD0, "SET_EOF"),
            (0xD1, "GET_EOF"),
            (0xD2, "SET_BUF"),
            (0xD3, "GET_BUF"),
        ];

        Assert.Multiple(() =>
        {
            foreach ((byte code, string name) in known)
            {
                Assert.That(ProDosMliCall.TryGetName(code, out string? found), Is.True, $"${code:X2}");
                Assert.That(found, Is.EqualTo(name));
                Assert.That(ProDosMliCall.FormatName(code), Is.EqualTo(name));
            }

            Assert.That(ProDosMliCall.TryGetName(0x1A, out string? unknown), Is.False);
            Assert.That(unknown, Is.Null);
            Assert.That(ProDosMliCall.FormatName(0x1A), Is.EqualTo("$1A (unknown)"));
            Assert.That(ProDosMliCall.FormatName(0x00), Is.EqualTo("$00 (unknown)"));
        });
    }

    /// <summary>The command is the byte after the stacked return, and the parameter pointer is the next word.</summary>
    [Test]
    public void Decode_ReadsCommandAndParameterPointer()
    {
        var memory = new GuestMemoryImage();
        memory.Poke(0x0303, 0xC8);
        memory.Poke(0x0304, 0x00);
        memory.Poke(0x0305, 0x04);

        ProDosMliCall call = ProDosMliCall.Decode(memory, 0x0302);

        Assert.Multiple(() =>
        {
            Assert.That(call.Number, Is.EqualTo(0xC8));
            Assert.That(call.Parameters, Is.EqualTo(0x0400));
        });
    }

    /// <summary>Path-carrying calls copy the length-prefixed pathname. RENAME copies both names.</summary>
    [Test]
    public void Pathname_IsCopiedForOpenCreateAndRename()
    {
        var memory = new GuestMemoryImage();
        PlantCall(memory, 0xC8, 0x0800, "/HELLO", null);
        ProDosMliCall open = ProDosMliCall.Decode(memory, 0x0302);
        Span<char> buffer = stackalloc char[ProDosMliCall.MaxPathnameLength];
        int count = open.CopyPrimaryPath(memory, buffer);
        string copied = buffer[..count].ToString();
        int renameOnly = open.CopyNewPath(memory, buffer);

        Assert.Multiple(() =>
        {
            Assert.That(ProDosMliCall.CarriesPathname(0xC8), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC0), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC1), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC4), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC6), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC2), Is.True);
            Assert.That(ProDosMliCall.CarriesPathname(0xC3), Is.False);
            Assert.That(ProDosMliCall.CarriesPathname(0xCA), Is.False);
            Assert.That(count, Is.EqualTo("/HELLO".Length));
            Assert.That(copied, Is.EqualTo("/HELLO"));
            Assert.That(renameOnly, Is.EqualTo(0));
        });

        PlantCall(memory, 0xC2, 0x0800, "/OLD", "/NEW");
        ProDosMliCall rename = ProDosMliCall.Decode(memory, 0x0302);
        int oldCount = rename.CopyPrimaryPath(memory, buffer);
        string oldPath = buffer[..oldCount].ToString();
        Span<char> second = stackalloc char[ProDosMliCall.MaxPathnameLength];
        int newCount = rename.CopyNewPath(memory, second);
        string newPath = second[..newCount].ToString();
        Assert.Multiple(() =>
        {
            Assert.That(oldPath, Is.EqualTo("/OLD"));
            Assert.That(newPath, Is.EqualTo("/NEW"));
        });
    }

    /// <summary><c>off</c>, <c>mli</c>, <c>dos</c>, <c>all</c>, and <c>on</c> parse. Anything else does not.</summary>
    [Test]
    public void TryParseMode_AcceptsTheProfileTokens()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OsCallLog.TryParseMode(null, out OsCallLogMode empty), Is.True);
            Assert.That(empty, Is.EqualTo(OsCallLogMode.Off));
            Assert.That(OsCallLog.TryParseMode("mli", out OsCallLogMode mli), Is.True);
            Assert.That(mli, Is.EqualTo(OsCallLogMode.Mli));
            Assert.That(OsCallLog.TryParseMode("DOS", out OsCallLogMode dos), Is.True);
            Assert.That(dos, Is.EqualTo(OsCallLogMode.Dos));
            Assert.That(OsCallLog.TryParseMode("on", out OsCallLogMode on), Is.True);
            Assert.That(on, Is.EqualTo(OsCallLogMode.All));
            Assert.That(OsCallLog.TryParseMode("all", out OsCallLogMode all), Is.True);
            Assert.That(all, Is.EqualTo(OsCallLogMode.All));
            Assert.That(OsCallLog.TryParseMode("both", out _), Is.False);
        });
    }

    private static void PlantCall(GuestMemoryImage memory, byte command, ushort parms, string path, string? newPath)
    {
        memory.Poke(0x0303, command);
        memory.Poke(0x0304, (byte)parms);
        memory.Poke(0x0305, (byte)(parms >> 8));
        ushort text = (ushort)(parms + 16);
        PlantPath(memory, text, path);
        memory.Poke(parms, 0x03);
        memory.Poke((ushort)(parms + 1), (byte)text);
        memory.Poke((ushort)(parms + 2), (byte)(text >> 8));
        if (newPath is null)
        {
            return;
        }

        ushort renamed = (ushort)(text + path.Length + 2);
        PlantPath(memory, renamed, newPath);
        memory.Poke((ushort)(parms + 3), (byte)renamed);
        memory.Poke((ushort)(parms + 4), (byte)(renamed >> 8));
    }

    private static void PlantPath(GuestMemoryImage memory, ushort address, string path)
    {
        memory.Poke(address, (byte)path.Length);
        for (int index = 0; index < path.Length; index++)
        {
            memory.Poke((ushort)(address + 1 + index), (byte)path[index]);
        }
    }
}