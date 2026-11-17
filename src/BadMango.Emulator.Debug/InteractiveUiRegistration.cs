// <copyright file="InteractiveUiRegistration.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug;

using System.Runtime.CompilerServices;

using Autofac;

using BadMango.Emulator.Debug.UI;

/// <summary>
/// Loads the Avalonia debug UI into the composition root.
/// </summary>
/// <remarks>
/// This type is the only compile-time reference from the debug host to
/// <c>BadMango.Emulator.Debug.UI</c>. The headless path must not call <see cref="Register"/>.
/// <see cref="MethodImplOptions.NoInlining"/> keeps that call out of the headless method body,
/// so the runtime does not load the UI assembly or Avalonia while resolving the headless path.
/// </remarks>
internal static class InteractiveUiRegistration
{
    /// <summary>
    /// Registers <see cref="DebugUiModule"/> with the interactive host container.
    /// </summary>
    /// <param name="builder">The host container builder.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RegisterModule<DebugUiModule>();
    }
}