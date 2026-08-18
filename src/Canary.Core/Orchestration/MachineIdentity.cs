using System.Runtime.InteropServices;

namespace Canary.Orchestration;

/// <summary>
/// Which machine a capture came from.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5b. The capture recorded <c>capturedUtc</c> and nothing that said
/// WHICH machine — and the entire point of the capture is to be diffed between two machines.
/// Two files that do not name their origin cannot be compared; you are left inferring it from
/// the paths inside, which is guesswork exactly when it matters.
/// </para>
/// <para>
/// It also catches a specific QC failure: a payload or results tree copied from one machine to
/// another carries the SOURCE machine's capture. Without identity, that reads as a clean local
/// capture — the machine appears to have been verified when nothing on it was ever probed.
/// </para>
/// <para>
/// No new exposure: the capture is already full of <c>C:\Users\&lt;name&gt;\AppData\…</c> paths,
/// and <c>results/</c> is gitignored. Stating identity plainly beats leaving it implicit in
/// path fragments.
/// </para>
/// </remarks>
public static class MachineIdentity
{
    /// <summary>Field name for the machine (host) name.</summary>
    public const string MachineName = "machineName";

    /// <summary>Field name for the OS description.</summary>
    public const string Os = "os";

    /// <summary>Field name for the interactive user.</summary>
    public const string User = "user";

    /// <summary>Field name for the process architecture.</summary>
    public const string Architecture = "architecture";

    /// <summary>Field name for the harness runtime.</summary>
    public const string Runtime = "runtime";

    /// <summary>Describes the machine this process is running on.</summary>
    /// <returns>Flat string map, ordered for a stable diff.</returns>
    public static IReadOnlyDictionary<string, string> Describe()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(map, MachineName, () => Environment.MachineName);
        Add(map, Os, () => RuntimeInformation.OSDescription);
        Add(map, User, () => Environment.UserName);
        Add(map, Architecture, () => RuntimeInformation.ProcessArchitecture.ToString());
        Add(map, Runtime, () => RuntimeInformation.FrameworkDescription);
        return map;
    }

    /// <summary>
    /// True when a captured identity describes the machine this process is running on.
    /// </summary>
    /// <param name="captured">Identity read from a capture, or null.</param>
    /// <returns>True only when the machine name matches.</returns>
    /// <remarks>
    /// Keyed on machine name alone, deliberately: OS build and runtime version move under a
    /// machine's feet without it becoming a different machine, and treating a Windows update as
    /// "this capture is from somewhere else" would cry wolf. An ABSENT machine name returns
    /// false — a capture that cannot say where it came from has not established that it came
    /// from here.
    /// </remarks>
    public static bool IsThisMachine(IReadOnlyDictionary<string, string>? captured)
    {
        if (captured == null) return false;
        if (!captured.TryGetValue(MachineName, out var name) || string.IsNullOrWhiteSpace(name))
            return false;
        return string.Equals(name.Trim(), Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renders the identity as one line.</summary>
    /// <param name="identity">Identity map, or null.</param>
    /// <returns>A short human-readable summary.</returns>
    public static string Format(IReadOnlyDictionary<string, string>? identity)
    {
        if (identity == null || identity.Count == 0) return "(machine not recorded)";
        identity.TryGetValue(MachineName, out var name);
        identity.TryGetValue(User, out var user);
        identity.TryGetValue(Os, out var os);
        var head = string.IsNullOrWhiteSpace(name) ? "(unnamed machine)" : name;
        if (!string.IsNullOrWhiteSpace(user)) head += $"\\{user}";
        return string.IsNullOrWhiteSpace(os) ? head : $"{head}  ·  {os}";
    }

    // A probe that throws must not cost the whole identity: a partial answer that names the
    // machine is still enough to compare two captures.
    private static void Add(Dictionary<string, string> map, string key, Func<string> read)
    {
        try
        {
            var value = read();
            if (!string.IsNullOrWhiteSpace(value)) map[key] = value.Trim();
        }
        catch
        {
            // Deliberately silent: recorded as absent, which Format and IsThisMachine handle.
        }
    }
}
