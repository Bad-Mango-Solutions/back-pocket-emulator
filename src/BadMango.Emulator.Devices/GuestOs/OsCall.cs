// <copyright file="OsCall.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Core;

/// <summary>
/// One decoded guest OS call. Also the argument of <see cref="IOsCallLog.Recorded"/>.
/// </summary>
public sealed class OsCall : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OsCall"/> class.
    /// </summary>
    /// <param name="at">The machine cycle when the call was entered.</param>
    /// <param name="os">The operating system that owns the call.</param>
    /// <param name="code">The command byte.</param>
    /// <param name="name">The command name. Not empty.</param>
    /// <param name="caller">The address of the calling instruction.</param>
    /// <param name="paramList">The parameter list, or the RWTS IOB.</param>
    /// <param name="summary">A pathname or RWTS location, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">If <paramref name="name"/> is empty.</exception>
    public OsCall(Cycle at, GuestOsId os, byte code, string name, Addr caller, Addr paramList, string? summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.At = at;
        this.Os = os;
        this.Code = code;
        this.Name = name;
        this.Caller = caller;
        this.ParamList = paramList;
        this.Summary = summary;
    }

    /// <summary>Gets the machine cycle when the call was entered.</summary>
    /// <value>The cycle stamp.</value>
    public Cycle At { get; }

    /// <summary>Gets the operating system that owns the call.</summary>
    /// <value><see cref="GuestOsId.ProDos8"/> or <see cref="GuestOsId.Dos33"/>.</value>
    public GuestOsId Os { get; }

    /// <summary>Gets the command byte.</summary>
    /// <value>The MLI command, the file-manager opcode, or the RWTS command.</value>
    public byte Code { get; }

    /// <summary>Gets the command name.</summary>
    /// <value>A TechRef name, or <c>$xx (unknown)</c>.</value>
    public string Name { get; }

    /// <summary>Gets the address of the calling instruction.</summary>
    /// <value>The <c>JSR</c> that entered the call.</value>
    public Addr Caller { get; }

    /// <summary>Gets the parameter list or IOB.</summary>
    /// <value>The MLI or file-manager parameter list, or the RWTS IOB address.</value>
    public Addr ParamList { get; }

    /// <summary>Gets the extra text for this call.</summary>
    /// <value>A pathname, an RWTS slot line, or <see langword="null"/>.</value>
    public string? Summary { get; }
}