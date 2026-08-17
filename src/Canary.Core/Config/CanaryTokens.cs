using System.Text.Json;

namespace Canary.Config;

/// <summary>
/// Resolves <c>%TOKEN%</c> values for test content from a layered source.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 2. The corpus carried ~389 absolute path literals across
/// 243 files, pointing at ten external roots — six peer repositories, a Drive handoff
/// folder, a user profile. Every one of them assumed this machine's layout, which is why
/// a machine without <c>C:\Repos</c> could not run the tests it had been shipped.
/// </para>
/// <para>
/// <b>The goal is NOT "no absolute paths".</b> Something must still say where Bristle's
/// fixtures live. The goal is that the answer exists in <b>one visible, overridable
/// place</b> instead of scattered through 243 test files where no one can find or change
/// it. Tokens make the dependency declarable; this class makes it resolvable.
/// </para>
/// <para>
/// Layering, highest first:
/// <list type="number">
/// <item><b>Environment variables</b> — how a QC machine or an installer overrides a
///   single root without editing content it did not author;</item>
/// <item><b><c>tokens.json</c> at the workloads root</b> — travels with the content, so
///   a harness pack can declare its own roots;</item>
/// <item><b>unset</b> — the token is left <b>exactly as written</b>. It is not silently
///   blanked, because a path that resolved to nothing would fail somewhere far from the
///   cause.</item>
/// </list>
/// </para>
/// </remarks>
public static class CanaryTokens
{
    /// <summary>File, relative to the workloads root, declaring token values.</summary>
    public const string TokensFileName = "tokens.json";

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _cache;
    private static string? _cacheKey;
    private static string? _parseError;

    /// <summary>
    /// Loads the token table for a workloads root, layering environment over file.
    /// </summary>
    /// <param name="workloadsRoot">Workloads root to read <c>tokens.json</c> from.</param>
    /// <returns>Token name (without percent signs) to value.</returns>
    public static IReadOnlyDictionary<string, string> Load(string workloadsRoot)
    {
        lock (Gate)
        {
            if (_cache != null && _cacheKey == workloadsRoot) return _cache;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _parseError = null;

            var file = Path.Combine(workloadsRoot, TokensFileName);
            if (File.Exists(file))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (parsed != null)
                        foreach (var kvp in parsed)
                        {
                            // A LEADING UNDERSCORE MEANS DOCUMENTATION, not a token. JSON
                            // has no comments, so tokens.json carries "_comment_N" keys.
                            // `canary doctor` found these being loaded as real tokens on
                            // its very first run and reported six errors for prose that
                            // "does not exist on this machine" - correct, and the reason
                            // the convention now has to be enforced here rather than only
                            // in the conversion script.
                            if (kvp.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                            map[kvp.Key] = kvp.Value;
                        }
                }
                catch (JsonException)
                {
                    // A malformed tokens.json must not be swallowed into "no tokens" -
                    // that would look identical to an absent file and every path would
                    // fail far from the cause. Kept in a FIELD rather than in the map: a
                    // sentinel key pollutes the token namespace and could collide with a
                    // real token name.
                    _parseError = file;
                }
            }

            // Environment last so it WINS: a QC machine overrides one root without
            // touching content it did not author.
            foreach (var key in map.Keys.ToList())
            {
                var env = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(env)) map[key] = env;
            }

            _cache = map;
            _cacheKey = workloadsRoot;
            return map;
        }
    }

    /// <summary>Clears the cached table. For tests, and after a tokens.json edit.</summary>
    public static void Invalidate()
    {
        lock (Gate) { _cache = null; _cacheKey = null; _parseError = null; }
    }

    /// <summary>
    /// Expands <c>%TOKEN%</c> occurrences using the table, then environment variables.
    /// </summary>
    /// <param name="value">Raw text from test content.</param>
    /// <param name="workloadsRoot">Workloads root whose <c>tokens.json</c> applies.</param>
    /// <returns>Text with known tokens substituted; unknown tokens left as written.</returns>
    public static string Expand(string? value, string workloadsRoot)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOf('%') < 0) return value;      // cheap out: most content has none

        var map = Load(workloadsRoot);
        var result = value;
        foreach (var kvp in map)
            result = result.Replace($"%{kvp.Key}%", kvp.Value, StringComparison.OrdinalIgnoreCase);

        // Environment variables still apply, so %LOCALAPPDATA% and friends keep working
        // exactly as they did before Phase 2.
        return CanaryPaths.Expand(result);
    }

    /// <summary>
    /// Expands <c>%TOKEN%</c> occurrences, resolving the workloads root itself.
    /// </summary>
    /// <param name="value">Raw text from configuration.</param>
    /// <returns>Text with known tokens substituted.</returns>
    /// <remarks>
    /// For static call sites that have no workloads root threaded to them - the process
    /// only ever has one, and <see cref="CanaryPaths"/> resolves it deterministically.
    /// </remarks>
    public static string Expand(string? value)
        => Expand(value, CanaryPaths.ResolveWorkloadsRoot());

    /// <summary>
    /// Returns every <c>%TOKEN%</c> in the text that nothing can resolve.
    /// </summary>
    /// <param name="value">Text to inspect.</param>
    /// <param name="workloadsRoot">Workloads root whose <c>tokens.json</c> applies.</param>
    /// <returns>Unresolvable token names, without percent signs.</returns>
    /// <remarks>
    /// This is what makes an unresolved token a <b>reportable</b> condition rather than a
    /// mystery: <c>canary doctor</c> can name exactly which roots a machine is missing
    /// before a run rather than after a failure.
    /// </remarks>
    public static IReadOnlyList<string> FindUnresolved(string? value, string workloadsRoot)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0) return missing;

        var map = Load(workloadsRoot);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(value, "%([A-Za-z_][A-Za-z0-9_]*)%"))
        {
            var name = m.Groups[1].Value;
            if (map.ContainsKey(name)) continue;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) continue;
            missing.Add(name);
        }
        return missing;
    }

    /// <summary>
    /// Describes a token-table problem, or <c>null</c> when the table is usable.
    /// </summary>
    /// <param name="workloadsRoot">Workloads root whose <c>tokens.json</c> applies.</param>
    /// <returns>A human-readable problem, or <c>null</c>.</returns>
    public static string? DescribeProblem(string workloadsRoot)
    {
        Load(workloadsRoot);            // populates _parseError as a side effect
        lock (Gate)
        {
            return _parseError is null
                ? null
                : $"{_parseError} is not valid JSON; no tokens were loaded from it";
        }
    }
}
