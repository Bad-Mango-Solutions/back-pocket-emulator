// <copyright file="MainBusZeroFaultTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using System.Reflection;
using System.Reflection.Emit;

/// <summary>
/// Successful <see cref="MainBus"/> accesses return a zero <see cref="BusFault"/>.
/// A failing <c>Try*</c> returns the same record the ring stores.
/// </summary>
[TestFixture]
public class MainBusZeroFaultTests
{
    /// <summary>
    /// A successful <see cref="MainBus.TryRead8"/> returns <see cref="FaultKind.None"/>
    /// and the default address, device, and region.
    /// </summary>
    [Test]
    public void TryRead8_MappedPage_ReturnsZeroFault()
    {
        var raw = new byte[0x1000];
        raw[0x0100] = 0x5A;
        var bus = new MainBus();
        bus.MapPage(0, new(7, RegionTag.Rom, PagePerms.ReadWrite, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));

        var result = bus.TryRead8(Access(0x0100, AccessIntent.DataRead));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Value, Is.EqualTo(0x5A));
            Assert.That(result.Cycles, Is.EqualTo(1ul));
            Assert.That(result.Fault, Is.EqualTo(default(BusFault)));
            Assert.That(result.Fault.Kind, Is.EqualTo(FaultKind.None));
            Assert.That(result.Fault.Address, Is.EqualTo(default(Addr)));
            Assert.That(result.Fault.DeviceId, Is.EqualTo(0));
            Assert.That(result.Fault.RegionTag, Is.EqualTo(default(RegionTag)));
        });
    }

    /// <summary>
    /// A successful debug <see cref="MainBus.TryRead8"/> returns a zero fault.
    /// The page's device and region stay off the success result.
    /// </summary>
    /// <remarks>
    /// <c>MachineDebugSession.ReadMemory</c> reads with this access shape.
    /// </remarks>
    [Test]
    public void TryRead8_DebugRead_MappedPage_ReturnsZeroFault()
    {
        var raw = new byte[0x1000];
        raw[0x0100] = 0x5A;
        var bus = new MainBus();
        bus.MapPage(0, new(7, RegionTag.Rom, PagePerms.ReadWrite, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));

        var result = bus.TryRead8(new BusAccess(
            Address: 0x0100,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Value, Is.EqualTo(0x5A));
            Assert.That(result.Fault, Is.EqualTo(default(BusFault)));
            Assert.That(result.Fault.Kind, Is.EqualTo(FaultKind.None));
            Assert.That(result.Fault.Address, Is.EqualTo(default(Addr)));
            Assert.That(result.Fault.DeviceId, Is.EqualTo(0));
            Assert.That(result.Fault.RegionTag, Is.EqualTo(default(RegionTag)));
        });
    }

    /// <summary>
    /// A successful <see cref="MainBus.TryWrite8"/> returns a zero fault.
    /// </summary>
    [Test]
    public void TryWrite8_MappedPage_ReturnsZeroFault()
    {
        var raw = new byte[0x1000];
        var bus = new MainBus();
        bus.MapPage(0, new(7, RegionTag.Rom, PagePerms.ReadWrite, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));

        var result = bus.TryWrite8(Access(0x0200, AccessIntent.DataWrite), 0x17);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(raw[0x0200], Is.EqualTo(0x17));
            Assert.That(result.Fault, Is.EqualTo(default(BusFault)));
            Assert.That(result.Fault.Kind, Is.EqualTo(FaultKind.None));
            Assert.That(result.Fault.Address, Is.EqualTo(default(Addr)));
            Assert.That(result.Fault.DeviceId, Is.EqualTo(0));
            Assert.That(result.Fault.RegionTag, Is.EqualTo(default(RegionTag)));
        });
    }

    /// <summary>
    /// An atomic wide success returns a zero fault rather than the access's address and device.
    /// </summary>
    [Test]
    public void TryRead16_AtomicWideSuccess_ReturnsZeroFault()
    {
        var raw = new byte[0x1000];
        raw[0] = 0x34;
        raw[1] = 0x12;
        var bus = new MainBus();
        bus.MapPage(
            0,
            new(7, RegionTag.Rom, PagePerms.ReadWrite, TargetCaps.SupportsPeek | TargetCaps.SupportsWide, new RamTarget(raw, "ram"), 0));

        var result = bus.TryRead16(Access(0x0000, AccessIntent.DataRead, BusAccessMode.Atomic, widthBits: 16));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Value, Is.EqualTo((Word)0x1234));
            Assert.That(result.Fault, Is.EqualTo(default(BusFault)));
        });
    }

    /// <summary>
    /// A direct <c>Try*</c> records the fault it returns, with <see cref="BusFault.Pc"/> at 0.
    /// </summary>
    [Test]
    public void TryRead8_DirectCall_RecordsTheReturnedFaultWithZeroPc()
    {
        var ring = new BusFaultRing();
        var bus = new MainBus(addressSpaceBits: 16, faultRecorder: ring) { FaultRecordingEnabled = true };

        var result = bus.TryRead8(Access(0x5000, AccessIntent.DataRead));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Pc, Is.EqualTo(0u));
            Assert.That(ring.Count, Is.EqualTo(1));
            Assert.That(ring.Last, Is.Not.Null);
            Assert.That(ring.Last!.Value, Is.EqualTo(result.Fault));
            Assert.That(bus.LatchedFault.Pc, Is.EqualTo(0u));
        });
    }

    /// <summary>
    /// A debug <c>Try*</c> still faults for the caller and still stays out of the ring.
    /// </summary>
    [Test]
    public void TryRead8_Debug_DoesNotEnterTheRing()
    {
        var ring = new BusFaultRing();
        var bus = new MainBus(addressSpaceBits: 16, faultRecorder: ring) { FaultRecordingEnabled = true };

        var result = bus.TryRead8(Access(0x5000, AccessIntent.DebugRead));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(ring.Count, Is.Zero);
            Assert.That(ring.TotalFaults, Is.Zero);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
        });
    }

    /// <summary>
    /// Production <see cref="MainBus"/> methods do not call the nine-field success factory.
    /// </summary>
    /// <remarks>
    /// The factory remains internal so <see cref="BusFaultTests"/> can call it. This test counts
    /// the call instructions. A known caller in <see cref="BusResult"/> is the check that the
    /// counter sees a real call.
    /// </remarks>
    [Test]
    public void MainBus_DoesNotCallNineFieldSuccessFactory()
    {
        MethodInfo knownCaller = NineFieldBusResultSuccess();
        Assert.That(CountNineFieldSuccessCalls(knownCaller), Is.EqualTo(1), "The counter must see the test-only factory call");

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        int calls = 0;
        foreach (MethodInfo method in typeof(MainBus).GetMethods(Flags))
        {
            calls += CountNineFieldSuccessCalls(method);
        }

        foreach (ConstructorInfo constructor in typeof(MainBus).GetConstructors(Flags))
        {
            calls += CountNineFieldSuccessCalls(constructor);
        }

        Assert.That(calls, Is.Zero);
    }

    private static MethodInfo NineFieldBusResultSuccess()
    {
        foreach (MethodInfo method in typeof(BusResult).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (method.Name != nameof(BusResult.Success))
            {
                continue;
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(BusAccess).MakeByRefType())
                {
                    return method;
                }
            }
        }

        throw new InvalidOperationException("BusResult.Success(in BusAccess, ...) was not found.");
    }

    private static int CountNineFieldSuccessCalls(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null || il.Length == 0)
        {
            return 0;
        }

        (int[] one, int[] two) = OperandSizes();
        int count = 0;
        int index = 0;
        while (index < il.Length)
        {
            byte operation = il[index++];
            int operand;
            if (operation == 0xFE)
            {
                if (index >= il.Length)
                {
                    break;
                }

                byte second = il[index++];
                operand = two[second];
            }
            else
            {
                operand = one[operation];
            }

            if (operand == -1)
            {
                Assert.Fail($"Unknown IL opcode 0x{operation:X2} in {method.Name}.");
            }

            if (operation is 0x28 or 0x6F
                && index + 4 <= il.Length
                && IsNineFieldSuccess(method, ReadInt32(il, index)))
            {
                count++;
            }

            if (operand == -2)
            {
                if (index + 4 > il.Length)
                {
                    break;
                }

                int cases = ReadInt32(il, index);
                index += 4 + (cases * 4);
                continue;
            }

            if (operand > 0)
            {
                index += operand;
            }
        }

        return count;
    }

    private static bool IsNineFieldSuccess(MethodBase caller, int token)
    {
        MethodBase? resolved;
        try
        {
            resolved = caller.Module.ResolveMethod(
                token,
                caller.DeclaringType?.GetGenericArguments(),
                caller.IsGenericMethodDefinition ? caller.GetGenericArguments() : Type.EmptyTypes);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (resolved is null || resolved.Name != nameof(BusFault.Success))
        {
            return false;
        }

        return resolved.GetParameters()
            .Select(parameter => parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()!
                : parameter.ParameterType)
            .Any(parameterType => parameterType == typeof(BusAccess));
    }

    private static int ReadInt32(byte[] il, int index) =>
        il[index] | (il[index + 1] << 8) | (il[index + 2] << 16) | (il[index + 3] << 24);

    private static (int[] One, int[] Two) OperandSizes()
    {
        var one = new int[256];
        var two = new int[256];
        Array.Fill(one, -1);
        Array.Fill(two, -1);
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
            {
                continue;
            }

            int size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                    or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                    or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => -2,
                _ => -1,
            };

            if (opcode.Size == 1)
            {
                one[(byte)opcode.Value] = size;
            }
            else if (opcode.Size == 2)
            {
                two[(byte)opcode.Value] = size;
            }
        }

        return (one, two);
    }

    private static BusAccess Access(
        Addr address,
        AccessIntent intent,
        BusAccessMode mode = BusAccessMode.Decomposed,
        byte widthBits = 8) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: mode,
            EmulationFlag: mode == BusAccessMode.Decomposed,
            Intent: intent,
            SourceId: 4,
            Cycle: 11,
            Flags: AccessFlags.None);
}