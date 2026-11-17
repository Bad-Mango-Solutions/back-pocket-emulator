// <copyright file="DebugMcpRunTracker.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// Remembers the background run started by this host so poll, stop, and pause can find it.
/// </summary>
public sealed class DebugMcpRunTracker
{
    private readonly Lock gate = new();
    private Guid? activeRunId;

    /// <summary>
    /// Gets the run identifier recorded by the latest <see cref="Remember"/>.
    /// </summary>
    public Guid? Current
    {
        get
        {
            lock (this.gate)
            {
                return this.activeRunId;
            }
        }
    }

    /// <summary>
    /// Records <paramref name="runId"/> as the active background run.
    /// </summary>
    /// <param name="runId">The run identifier returned by the session.</param>
    public void Remember(Guid runId)
    {
        lock (this.gate)
        {
            this.activeRunId = runId;
        }
    }

    /// <summary>
    /// Forgets <paramref name="runId"/> when it is still the recorded run.
    /// </summary>
    /// <param name="runId">The run identifier to clear.</param>
    public void Clear(Guid runId)
    {
        lock (this.gate)
        {
            if (this.activeRunId == runId)
            {
                this.activeRunId = null;
            }
        }
    }
}