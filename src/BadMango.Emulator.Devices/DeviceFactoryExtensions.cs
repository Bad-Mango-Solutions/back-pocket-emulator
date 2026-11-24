// <copyright file="DeviceFactoryExtensions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices.Interfaces;
using BadMango.Emulator.Devices.Speaker;

/// <summary>
/// Extension methods for <see cref="MachineBuilder"/> providing motherboard device factory registration.
/// </summary>
/// <remarks>
/// <para>
/// These extensions provide device factory registration for profile-based machine loading.
/// They allow profiles to declare devices in the <c>devices.motherboard</c> section that
/// are created when the profile is loaded.
/// </para>
/// <para>
/// The preferred approach is to use <see cref="DeviceFactoryRegistry.RegisterAllDeviceFactories"/>
/// which auto-discovers all devices marked with <see cref="DeviceTypeAttribute"/>. The manual
/// registration methods are retained for backward compatibility and special cases.
/// </para>
/// </remarks>
public static class DeviceFactoryExtensions
{
    /// <summary>
    /// Extension block for <see cref="MachineBuilder"/> providing device factory registration.
    /// </summary>
    extension(MachineBuilder builder)
    {
        /// <summary>
        /// Registers the speaker device factory for profile-based loading.
        /// </summary>
        /// <returns>This builder instance for method chaining.</returns>
        /// <remarks>
        /// <para>
        /// This method registers the "speaker" motherboard device factory. When a profile
        /// includes a speaker device in its <c>devices.motherboard</c> section, this factory
        /// creates a <see cref="SpeakerController"/> instance.
        /// </para>
        /// <para>
        /// The speaker controller handles the $C030 soft switch and records toggle events
        /// for audio synthesis. It receives the logger from
        /// <see cref="DeviceFactoryRegistry.LoggerFactory"/> (or a sink-less logger when none is
        /// configured) so host audio failures and the silent-output fallback are reported.
        /// </para>
        /// <para>
        /// Prefer using <see cref="RegisterStandardDeviceFactories"/> which registers all
        /// known device factories automatically.
        /// </para>
        /// </remarks>
        public MachineBuilder RegisterSpeakerDeviceFactory()
        {
            return builder.RegisterMotherboardDeviceFactory(
                "speaker",
                _ => new SpeakerController(DeviceFactoryRegistry.ResolveLogger(typeof(SpeakerController))));
        }

        /// <summary>
        /// Registers the PocketWatch slot card factory for profile-based loading.
        /// </summary>
        /// <returns>This builder instance for method chaining.</returns>
        /// <remarks>
        /// <para>
        /// This method registers the "pocketwatch" slot card factory. When a profile
        /// includes a PocketWatch card in its <c>devices.slots.cards</c> section, this factory
        /// creates a <see cref="PocketWatchCard"/> from that card's <c>config</c> object.
        /// A null, missing, or unreadable config keeps the card defaults. A read failure is logged
        /// when <see cref="DeviceFactoryRegistry.LoggerFactory"/> is configured.
        /// </para>
        /// <para>
        /// Prefer using <see cref="RegisterStandardDeviceFactories"/> which registers all
        /// known device factories automatically. That path deserializes the same config.
        /// </para>
        /// </remarks>
        public MachineBuilder RegisterPocketWatchCardFactory()
        {
            return builder.RegisterSlotCardFactory(
                "pocketwatch",
                (_, cardConfig) => PocketWatchCard.FromProfileConfig(
                    cardConfig,
                    DeviceFactoryRegistry.ResolveLogger(typeof(PocketWatchCard))));
        }

        /// <summary>
        /// Registers all standard device factories for profile-based loading using auto-discovery.
        /// </summary>
        /// <returns>This builder instance for method chaining.</returns>
        /// <remarks>
        /// <para>
        /// This method uses reflection to discover all device types marked with
        /// <see cref="DeviceTypeAttribute"/> and registers their factories automatically.
        /// This includes both motherboard devices and slot cards.
        /// </para>
        /// <para>
        /// Additionally, this method registers device factories that require special
        /// configuration beyond simple instantiation, such as the Language Card and
        /// Extended 80-Column Card which need memory layers configured.
        /// </para>
        /// <para>
        /// Currently registered devices:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Motherboard: Speaker ($C030)</description></item>
        /// <item><description>Motherboard: Language Card (16KB RAM at $D000-$FFFF)</description></item>
        /// <item><description>Motherboard: Extended 80-Column Card (64KB auxiliary RAM)</description></item>
        /// <item><description>Slot Cards: PocketWatch (Thunderclock-compatible RTC)</description></item>
        /// </list>
        /// <para>
        /// Additional devices are automatically registered as they are added with
        /// the <see cref="DeviceTypeAttribute"/>.
        /// </para>
        /// </remarks>
        public MachineBuilder RegisterStandardDeviceFactories()
        {
            // Register auto-discovered device factories (simple devices with parameterless constructors)
            builder.RegisterAllDeviceFactories();

            // Register devices that require special memory configuration.
            // The Language Card needs memory layers and swap groups configured, which can't
            // be done through simple auto-discovery. We use a placeholder ROM target that
            // allows the underlying base ROM mapping to show through when LC RAM is disabled.
            builder.RegisterLanguageCardFactory();

            // The Extended 80-Column Card also needs special memory configuration for
            // auxiliary RAM layers (zero page, stack, text page, hi-res pages).
            builder.RegisterExtended80ColumnFactory();

            // One KeyboardInput per machine, created after the sink exists. Machines with no
            // IHostKeyboardSink get no input component.
            builder.RegisterKeyboardInputFactory();

            return builder;
        }

        /// <summary>
        /// Registers the factory that adds one <see cref="KeyboardInput"/> when the machine has a keyboard sink.
        /// </summary>
        /// <returns>This builder instance for method chaining.</returns>
        /// <remarks>
        /// The factory runs at the end of <see cref="MachineBuilder.Build"/>. It uses the machine's
        /// scheduler and dispatcher, and <see cref="MachineBuilder.ProfileCpuClockHz"/>
        /// (<c>cpu.clockSpeed</c>, or 1 MHz when the profile does not set one). It does not use
        /// <see cref="IMachine.ClockSpeedHz"/>, which is 0 when the machine is unthrottled.
        /// A second registration does not add a second input.
        /// </remarks>
        public MachineBuilder RegisterKeyboardInputFactory()
        {
            return builder.AddComponentFactory<IKeyboardInput>(machine =>
            {
                if (machine.GetComponent<IKeyboardInput>() is not null)
                {
                    return null;
                }

                IHostKeyboardSink? sink = machine.GetComponent<IHostKeyboardSink>();
                if (sink is null)
                {
                    return null;
                }

                return new KeyboardInput(sink, machine.Scheduler, machine, builder.ProfileCpuClockHz);
            });
        }
    }
}