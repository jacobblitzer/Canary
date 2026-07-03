using System;

namespace Canary.Agent;

/// <summary>
/// Optional capability interface for agents that own a spawned OS process (today: the Rhino
/// session agent). Lets the session layer record a death certificate — exit code + time — when
/// the process dies before session close-out (flight-recorder Phase A, gap G-G). This is the
/// SURVIVOR-side crash record: hard kills and native access violations fire no in-process hooks,
/// so the watcher must live in Canary, which outlives the target app.
/// </summary>
public interface IProcessBackedAgent
{
    /// <summary>OS process id of the spawned application.</summary>
    int ProcessId { get; }

    /// <summary>True once the spawned process has exited (for any reason, including our own kill).</summary>
    bool ProcessHasExited { get; }

    /// <summary>
    /// Exit facts, when the process has exited. Returns false while it is still running or when
    /// exit facts could not be read.
    /// </summary>
    bool TryGetProcessExit(out int exitCode, out DateTime exitUtc);
}
