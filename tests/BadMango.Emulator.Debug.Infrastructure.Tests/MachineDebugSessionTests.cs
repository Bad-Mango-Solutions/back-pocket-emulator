// <copyright file="MachineDebugSessionTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Abstractions;

using Moq;

/// <summary>
/// Contract and focused behavior tests for <see cref="MachineDebugSession"/>.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionTests : IDebugSessionContractTests
{
    /// <summary>
    /// Verifies run faults are surfaced as <see cref="StopReason.Fault"/> and leave the machine stopped.
    /// </summary>
    [Test]
    public void RunUntil_WhenStepThrows_ReturnsFaultAndStopsMachine()
    {
        var cpu = new Mock<ICpu>(MockBehavior.Strict);
        cpu.SetupGet(x => x.Halted).Returns(false);
        cpu.Setup(x => x.GetPC()).Returns(0x1000u);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.SetupGet(x => x.Cpu).Returns(cpu.Object);
        machine.Setup(x => x.Step()).Throws(new InvalidOperationException("simulated run fault"));
        machine.Setup(x => x.Stop());

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Machine).Returns(machine.Object);
        context.SetupGet(x => x.Breakpoints).Returns(new BreakpointManager());
        context.SetupGet(x => x.Watchpoints).Returns(new WatchpointManager());

        var session = new MachineDebugSession(context.Object);
        RunResult result = session.RunUntil(
            new(RunConditionKind.ProgramCounter, Address: 0x2000),
            new(MaxCycles: 100));

        Assert.That(result.StopReason, Is.EqualTo(StopReason.Fault));
        machine.Verify(x => x.Stop(), Times.Once);
    }

    /// <inheritdoc />
    protected override ISessionHarness CreateHarness() => new MachineSessionHarness();

    private sealed class MachineSessionHarness : ISessionHarness
    {
        private readonly DebugContext context;
        private readonly IMachine machine;

        public MachineSessionHarness()
        {
            var dispatcher = new CommandDispatcher();
            context = new(dispatcher, TextWriter.Null, TextWriter.Null);

            (machine, var disassembler, var info) = MachineFactory.CreateDebugSystem(CreateTestProfile());
            context.AttachMachine(machine, disassembler, info);
            Session = new MachineDebugSession(context);
        }

        public IDebugSession Session { get; }

        public void LoadProgram(ushort startAddress, params byte[] bytes)
        {
            Session.WriteMemory(startAddress, bytes);
            Session.WriteMemory(0xFFFC, [(byte)(startAddress & 0xFF), (byte)(startAddress >> 8)]);
            Session.Reset(ResetKind.Warm);
        }

        public void Dispose()
        {
            machine.Stop();
            context.Dispose();
        }

        private static MachineProfile CreateTestProfile()
        {
            return new()
            {
                Name = "machine-debug-session-test",
                DisplayName = "Machine Debug Session Test",
                Cpu = new() { Type = "65C02" },
                AddressSpace = 16,
                Memory = new()
                {
                    Physical =
                    [
                        new()
                        {
                            Name = "main-ram-64k",
                            Size = "0x10000",
                            Fill = "0x00",
                        },
                    ],
                    Regions =
                    [
                        new()
                        {
                            Name = "main-ram",
                            Type = "ram",
                            Start = "0x0000",
                            Size = "0x10000",
                            Permissions = "rwx",
                            Source = "main-ram-64k",
                            SourceOffset = "0x0000",
                        },
                    ],
                },
            };
        }
    }
}