namespace Canary.Config;

/// <summary>One requirement and why it was not met.</summary>
/// <param name="Requirement">The declaration that failed.</param>
/// <param name="Reason">What was actually found.</param>
/// <param name="DeclaredBy">Workload or test that declared it.</param>
public readonly record struct RequirementMiss(Requirement Requirement, string Reason, string DeclaredBy)
{
    /// <summary>The operator-facing line, including the fix when one was declared.</summary>
    /// <returns>Formatted message.</returns>
    public string Format()
    {
        var s = $"{Requirement.Describe()} — {Reason}  [declared by {DeclaredBy}]";
        if (!string.IsNullOrWhiteSpace(Requirement.Fix))
            s += $"{Environment.NewLine}      fix: {Requirement.Fix}";
        return s;
    }
}

/// <summary>
/// Checks the requirements that can be verified without launching the target application.
/// </summary>
/// <remarks>
/// Deployment campaign Phase 5. <c>file</c> and <c>service</c> are a syscall and one HTTP
/// GET respectively, both from the harness process, so this runs in <c>canary doctor</c>
/// with no app launch. <c>plugin</c> is not handled here by design — Grasshopper's library
/// table and Rhino's plug-in table exist only inside Rhino, so that half is asked of the
/// agent through <c>GetHostState</c> once the app is up.
/// </remarks>
public static class RequirementChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>
    /// Checks every offline-checkable requirement and returns those not met.
    /// </summary>
    /// <param name="requirements">Declarations to check, paired with who declared them.</param>
    /// <param name="workloadsRoot">Workloads root, for token expansion.</param>
    /// <returns>The misses, in declaration order.</returns>
    /// <remarks>
    /// Unknown kinds are reported as misses rather than skipped. A typo'd <c>kind</c> that
    /// silently passed would be a requirement that looks declared and is never checked,
    /// which is worse than not declaring it — the same shape as the defects this whole
    /// campaign exists to remove.
    /// </remarks>
    public static async Task<IReadOnlyList<RequirementMiss>> CheckOfflineAsync(
        IEnumerable<(Requirement Requirement, string DeclaredBy)> requirements, string workloadsRoot)
    {
        var misses = new List<RequirementMiss>();

        foreach (var (req, who) in requirements)
        {
            switch ((req.Kind ?? string.Empty).ToLowerInvariant())
            {
                case Requirement.KindFile:
                {
                    if (string.IsNullOrWhiteSpace(req.Path))
                    {
                        misses.Add(new RequirementMiss(req, "declares kind 'file' with no 'path'", who));
                        break;
                    }
                    var path = CanaryTokens.Expand(req.Path, workloadsRoot);
                    // A directory satisfies it too: node_modules, dist/ and decoded bundles
                    // are all "this must be here", and distinguishing them buys nothing.
                    if (!File.Exists(path) && !Directory.Exists(path))
                        misses.Add(new RequirementMiss(req, $"not found at {path}", who));
                    break;
                }

                case Requirement.KindService:
                {
                    if (string.IsNullOrWhiteSpace(req.Url))
                    {
                        misses.Add(new RequirementMiss(req, "declares kind 'service' with no 'url'", who));
                        break;
                    }
                    var url = CanaryTokens.Expand(req.Url, workloadsRoot);
                    try
                    {
                        using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            misses.Add(new RequirementMiss(req, $"answered {(int)resp.StatusCode}", who));
                            break;
                        }
                        if (!string.IsNullOrWhiteSpace(req.Contains))
                        {
                            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (body.IndexOf(req.Contains, StringComparison.OrdinalIgnoreCase) < 0)
                                misses.Add(new RequirementMiss(
                                    req, $"answered 2xx but the body does not contain '{req.Contains}'", who));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unreachable, refused, DNS, timeout - all the same answer to the
                        // only question being asked: is it there.
                        misses.Add(new RequirementMiss(req, $"unreachable ({ex.GetType().Name})", who));
                    }
                    break;
                }

                case Requirement.KindPlugin:
                    // Checked in-app via GetHostState; not a miss here.
                    break;

                default:
                    misses.Add(new RequirementMiss(
                        req, $"unknown kind '{req.Kind}' — expected file, service or plugin", who));
                    break;
            }
        }

        return misses;
    }

    /// <summary>
    /// Collects a workload's requirements plus those of the tests in scope.
    /// </summary>
    /// <param name="workload">Workload config, or null.</param>
    /// <param name="tests">Tests in scope.</param>
    /// <param name="workloadName">Name used to attribute the workload's own declarations.</param>
    /// <returns>Requirements paired with who declared each, de-duplicated.</returns>
    /// <remarks>
    /// Inheritance is <b>union, additive only</b>: a test may add a requirement, never
    /// remove one the workload declared. Subtraction is how a machine talks itself into
    /// running something it cannot.
    /// </remarks>
    /// <summary>
    /// The declared <c>origin</c> expectations, keyed by plug-in id.
    /// </summary>
    /// <param name="declared">Result of <see cref="Collect"/>.</param>
    /// <returns>Id → pin, for plug-in requirements that actually pin one.</returns>
    /// <remarks>
    /// Requirements with no pin, or pinned to <c>any</c>, are omitted rather than included as
    /// "any": the environment report judges origin ONLY where an expectation exists, and an
    /// entry meaning "no expectation" would be indistinguishable from one meaning "expected and
    /// satisfied" to anyone counting the map.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ExpectedOrigins(
        IEnumerable<(Requirement Requirement, string DeclaredBy)> declared)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (req, _) in declared)
        {
            if (!string.Equals(req.Kind, Requirement.KindPlugin, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(req.Id) || string.IsNullOrWhiteSpace(req.Origin)) continue;
            if (string.Equals(req.Origin.Trim(), "any", StringComparison.OrdinalIgnoreCase)) continue;
            map[req.Id.Trim()] = req.Origin.Trim();
        }
        return map;
    }

    public static IReadOnlyList<(Requirement Requirement, string DeclaredBy)> Collect(
        WorkloadConfig? workload, IEnumerable<TestDefinition> tests, string workloadName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(Requirement, string)>();

        void Add(Requirement r, string who)
        {
            var key = $"{r.Kind}|{r.Path}|{r.Url}|{r.Contains}|{r.Id}";
            if (seen.Add(key)) all.Add((r, who));
        }

        if (workload?.Requires != null)
            foreach (var r in workload.Requires) Add(r, $"workload {workloadName}");

        foreach (var t in tests)
            if (t.Requires != null)
                foreach (var r in t.Requires) Add(r, $"test {t.Name}");

        return all;
    }
}
