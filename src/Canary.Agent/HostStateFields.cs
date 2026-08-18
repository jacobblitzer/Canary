namespace Canary.Agent;

/// <summary>
/// The field names and id prefixes of the <c>GetHostState</c> contract, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5. These are constants rather than string literals for a
/// specific, already-paid reason: the Rhino agent originally emitted a field called
/// <c>ghLibraries</c> while the harness parsed one called <c>loaded</c>. Both halves were
/// correct in isolation, they were written in separate passes, and nothing connected them —
/// so the harness saw an empty map and reported two plug-ins as <b>absent on a machine where
/// both were loaded</b>. A false red blocks a healthy install, which is a different mistake
/// from passing a broken one and no better.
/// </para>
/// <para>
/// Three agents and one reader now share these symbols, so that mismatch becomes a compile
/// error instead of an empty dictionary.
/// </para>
/// </remarks>
public static class HostStateFields
{
    /// <summary>The action name every agent answers.</summary>
    public const string Action = "GetHostState";

    /// <summary>
    /// Newline-delimited <c>id=detail</c> rows, ids in the SAME namespace a requirement uses.
    /// </summary>
    public const string Loaded = "loaded";

    /// <summary>
    /// Whether the host could actually see its own plug-in/hook table yet.
    /// </summary>
    /// <remarks>
    /// Load-bearing. If the host has not finished initialising, "not in the list" means
    /// <b>"I do not know"</b>, not "missing" — and a reader that conflates them fails a
    /// healthy machine. Absence of evidence is not evidence of absence, so a requirement is
    /// only judged when this is <c>true</c>.
    /// </remarks>
    public const string HostReady = "hostReady";

    /// <summary>Newline-delimited load failures; a library that failed is invisible from <see cref="Loaded"/>.</summary>
    public const string LoadErrors = "loadErrors";

    /// <summary>Which kind of host answered: <c>rhino</c>, <c>chrome</c>, <c>webview2</c>.</summary>
    public const string Host = "host";

    /// <summary>Host application version.</summary>
    public const string HostVersion = "hostVersion";

    /// <summary>Runtime/framework the host is on, where it matters for binding.</summary>
    public const string Framework = "framework";

    /// <summary>Sections of the probe that could not be read, so a partial answer is honest.</summary>
    public const string PartialFailures = "partialFailures";

    /// <summary>Id prefix for a Grasshopper library.</summary>
    public const string GrasshopperPrefix = "gh:";

    /// <summary>Id prefix for a Rhino plug-in.</summary>
    public const string RhinoPrefix = "rhino:";

    /// <summary>Id prefix for a page-level JavaScript hook.</summary>
    public const string JsPrefix = "js:";
}
