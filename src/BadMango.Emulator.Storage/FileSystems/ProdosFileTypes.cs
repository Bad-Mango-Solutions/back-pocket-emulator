// <copyright file="ProdosFileTypes.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using System.Globalization;

/// <summary>Catalog names for ProDOS file-type bytes.</summary>
public static class ProdosFileTypes
{
    /// <summary>
    /// Returns the three-letter catalog name, or <c>$xx</c> when the byte has no standard name.
    /// </summary>
    /// <param name="fileType">The directory file-type byte.</param>
    /// <returns>A name such as <c>BAS</c>, or <c>$F1</c>.</returns>
    public static string CatalogName(byte fileType) => fileType switch
    {
        (byte)ProdosFileType.Bad => "BAD",
        (byte)ProdosFileType.Text => "TXT",
        (byte)ProdosFileType.Binary => "BIN",
        (byte)ProdosFileType.Directory => "DIR",
        (byte)ProdosFileType.AppleWorksDatabase => "ADB",
        (byte)ProdosFileType.AppleWorksWordProcessor => "AWP",
        (byte)ProdosFileType.AppleWorksSpreadsheet => "ASP",
        (byte)ProdosFileType.GsOsSystem => "S16",
        (byte)ProdosFileType.Pascal => "PAS",
        (byte)ProdosFileType.Command => "CMD",
        (byte)ProdosFileType.Integer => "INT",
        (byte)ProdosFileType.IntegerVariables => "IVR",
        (byte)ProdosFileType.Applesoft => "BAS",
        (byte)ProdosFileType.ApplesoftVariables => "VAR",
        (byte)ProdosFileType.Relocatable => "REL",
        (byte)ProdosFileType.Sys => "SYS",
        _ => "$" + fileType.ToString("X2", CultureInfo.InvariantCulture),
    };
}