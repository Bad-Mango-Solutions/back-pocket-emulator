// <copyright file="FileLoadSize.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// How big a mapped file is, and how many data units the map recorded for it.
/// </summary>
/// <param name="Length">End-of-file, in bytes.</param>
/// <param name="Units">Data blocks or data sectors that belong to the file. Index and catalog units are omitted.</param>
public readonly record struct FileLoadSize(long Length, int Units);