// <copyright file="DeviceDebugCommandsModule.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Reflection;

using Autofac;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Storage.FileSystems;

using Serilog;

using AutofacModule = Autofac.Module;

/// <summary>
/// Autofac module that auto-discovers and registers device-specific debug commands.
/// </summary>
/// <remarks>
/// <para>
/// This module scans for classes marked with <see cref="DeviceDebugCommandAttribute"/>
/// that implement <see cref="ICommandHandler"/> and registers them with the container.
/// </para>
/// <para>
/// Commands can have either:
/// <list type="bullet">
/// <item><description>A parameterless constructor (simple registration)</description></item>
/// <item><description>A constructor taking an optional <see cref="IDebugWindowManager"/> parameter
/// (registered with window manager injection)</description></item>
/// </list>
/// </para>
/// <para>
/// Device debug commands provide device-specific debugging functionality, such as
/// interpreting memory as device-specific data structures. They are automatically
/// added to the debug console when this module is loaded.
/// </para>
/// </remarks>
public class DeviceDebugCommandsModule : AutofacModule
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<AppleFileSystemModule>();

        // Scan the Debug.Infrastructure assembly for device debug commands
        var assembly = typeof(DeviceDebugCommandsModule).Assembly;

        var commandTypes = assembly.GetTypes()
            .Where(IsDeviceDebugCommand)
            .ToList();

        foreach (var commandType in commandTypes)
        {
            if (HasDetectorConstructor(commandType))
            {
                RegisterWithDetector(builder, commandType);
            }
            else if (HasSessionConstructor(commandType))
            {
                RegisterWithSession(builder, commandType);
            }
            else if (HasWindowManagerConstructor(commandType))
            {
                // Register with window manager injection using a lambda
                RegisterWithWindowManager(builder, commandType);
            }
            else
            {
                // Simple registration for parameterless constructor
                builder.RegisterType(commandType)
                    .As<ICommandHandler>()
                    .SingleInstance();
            }
        }
    }

    /// <summary>
    /// Determines if a type is a device debug command.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> if the type is a device debug command; otherwise, <see langword="false"/>.</returns>
    private static bool IsDeviceDebugCommand(Type type)
    {
        // Must be a concrete class
        if (type.IsAbstract || type.IsInterface)
        {
            return false;
        }

        // Must have the DeviceDebugCommandAttribute
        if (type.GetCustomAttribute<DeviceDebugCommandAttribute>() is null)
        {
            return false;
        }

        // Must implement ICommandHandler
        if (!typeof(ICommandHandler).IsAssignableFrom(type))
        {
            return false;
        }

        // Must have a parameterless constructor, a detector constructor, a session constructor,
        // or one taking IDebugWindowManager.
        bool hasParameterless = type.GetConstructor(Type.EmptyTypes) is not null;
        bool hasWindowManager = HasWindowManagerConstructor(type);
        bool hasSession = HasSessionConstructor(type);
        bool hasDetector = HasDetectorConstructor(type);

        return hasParameterless || hasWindowManager || hasSession || hasDetector;
    }

    /// <summary>
    /// Determines if a type has a constructor whose first parameter is <see cref="IAppleFileSystemDetector"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>
    /// <see langword="true"/> when the first parameter is the detector and any further parameters are
    /// <see cref="IDebugOutputRenderer"/>, <see cref="ILogger"/>, or <see cref="IDebugExportRoot"/>.
    /// </returns>
    private static bool HasDetectorConstructor(Type type)
    {
        return type.GetConstructors().Any(IsDetectorConstructor);
    }

    private static bool IsDetectorConstructor(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters();
        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IAppleFileSystemDetector))
        {
            return false;
        }

        for (int i = 1; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            if (parameterType != typeof(IDebugOutputRenderer)
                && parameterType != typeof(ILogger)
                && parameterType != typeof(IDebugExportRoot))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines if a type has a constructor that takes an <see cref="IMachineIoSession"/> parameter.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>
    /// <see langword="true"/> if the type has a constructor whose first parameter is
    /// <see cref="IMachineIoSession"/>; otherwise, <see langword="false"/>.
    /// An optional <see cref="IDebugOutputRenderer"/> parameter may follow.
    /// </returns>
    private static bool HasSessionConstructor(Type type)
    {
        return type.GetConstructors().Any(IsSessionConstructor);
    }

    private static bool IsSessionConstructor(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters();
        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IMachineIoSession))
        {
            return false;
        }

        for (int i = 1; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType != typeof(IDebugOutputRenderer))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines if a type has a constructor that takes an <see cref="IDebugWindowManager"/> parameter.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>
    /// <see langword="true"/> if the type has a constructor with a single
    /// <see cref="IDebugWindowManager"/> parameter; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool HasWindowManagerConstructor(Type type)
    {
        return type.GetConstructors()
            .Any(ctor =>
            {
                var parameters = ctor.GetParameters();
                return parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(IDebugWindowManager);
            });
    }

    /// <summary>
    /// Registers a command type that requires <see cref="IMachineIoSession"/> injection.
    /// </summary>
    /// <param name="builder">The container builder.</param>
    /// <param name="commandType">The command type to register.</param>
    private static void RegisterWithSession(ContainerBuilder builder, Type commandType)
    {
        builder.Register(ctx =>
        {
            var session = ctx.ResolveOptional<IMachineIoSession>();
            var renderer = ctx.ResolveOptional<IDebugOutputRenderer>();
            var constructor = commandType.GetConstructors()
                .Where(IsSessionConstructor)
                .OrderByDescending(ctor => ctor.GetParameters().Length)
                .First();
            object?[] arguments = new object?[constructor.GetParameters().Length];
            var parameters = constructor.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = parameters[i].ParameterType == typeof(IDebugOutputRenderer) ? renderer : session;
            }

            return (ICommandHandler)constructor.Invoke(arguments);
        })
        .As<ICommandHandler>()
        .SingleInstance();
    }

    /// <summary>
    /// Registers a command type that requires <see cref="IAppleFileSystemDetector"/> injection.
    /// </summary>
    /// <param name="builder">The container builder.</param>
    /// <param name="commandType">The command type to register.</param>
    private static void RegisterWithDetector(ContainerBuilder builder, Type commandType)
    {
        builder.Register(ctx =>
        {
            var constructor = commandType.GetConstructors()
                .Where(IsDetectorConstructor)
                .OrderByDescending(ctor => ctor.GetParameters().Length)
                .First();
            var parameters = constructor.GetParameters();
            object?[] arguments = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = ResolveDetectorArgument(ctx, parameters[i].ParameterType);
            }

            return (ICommandHandler)constructor.Invoke(arguments);
        })
        .As<ICommandHandler>()
        .SingleInstance();
    }

    private static object? ResolveDetectorArgument(IComponentContext context, Type parameterType)
    {
        if (parameterType == typeof(IAppleFileSystemDetector))
        {
            return context.Resolve<IAppleFileSystemDetector>();
        }

        if (parameterType == typeof(IDebugOutputRenderer))
        {
            return context.ResolveOptional<IDebugOutputRenderer>();
        }

        if (parameterType == typeof(ILogger))
        {
            return context.ResolveOptional<ILogger>();
        }

        return context.ResolveOptional<IDebugExportRoot>();
    }

    /// <summary>
    /// Registers a command type that requires <see cref="IDebugWindowManager"/> injection.
    /// </summary>
    /// <param name="builder">The container builder.</param>
    /// <param name="commandType">The command type to register.</param>
    private static void RegisterWithWindowManager(ContainerBuilder builder, Type commandType)
    {
        // Use reflection to create a lambda registration that resolves IDebugWindowManager optionally
        builder.Register(ctx =>
        {
            var windowManager = ctx.ResolveOptional<IDebugWindowManager>();
            var constructor = commandType.GetConstructors()
                .First(ctor =>
                {
                    var parameters = ctor.GetParameters();
                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(IDebugWindowManager);
                });

            return (ICommandHandler)constructor.Invoke([windowManager]);
        })
        .As<ICommandHandler>()
        .SingleInstance();
    }
}