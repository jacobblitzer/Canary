using System.Text.Json;
using Canary.Config;
using Xunit;

namespace Canary.Tests.Config;

/// <summary>
/// Deployment campaign Phase 2, corpus-level guards.
/// </summary>
/// <remarks>
/// The unit tests for <c>CanaryTokens</c> prove the mechanism. These prove the mechanism
/// and the actual shipped content agree - which is the part that would otherwise only be
/// discovered on a machine that could not run the tests it had been given.
/// </remarks>
public class TokenCorpusTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string WorkloadsRoot() => Path.Combine(RepoRoot(), "workloads");

    /// <summary>
    /// Matches a Windows drive path inside a parsed string.
    /// </summary>
    /// <remarks>
    /// The negative lookbehind is load-bearing. A drive letter is never preceded by
    /// another letter, but ordinary prose is full of things that look like one: a
    /// JavaScript blob containing <c>responsibilities:\n-</c> matched the naive pattern as
    /// drive "s", and a markdown fixture containing <c>links:\n</c> as drive "s" again.
    /// Requiring the boundary separates a real path from a word that merely ends in a
    /// letter before a colon.
    /// </remarks>
    private const string DrivePathPattern = @"(?<![A-Za-z])[A-Za-z]:[\\/][A-Za-z]";

    /// <summary>Every string value in a JSON document, at any depth.</summary>
    private static IEnumerable<string> Strings(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var v = e.GetString();
                if (v != null) yield return v;
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var s in Strings(p.Value)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray())
                    foreach (var s in Strings(i)) yield return s;
                break;
        }
    }

    private static IEnumerable<string> ContentFiles()
    {
        var root = WorkloadsRoot();
        foreach (var f in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/results/") || rel.Contains("/sessions/")) continue;
            if (rel.Equals("tokens.json", StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
    }

    // The Phase 2 exit criterion, asserted against the real corpus rather than a count in
    // a document. Any absolute drive path outside tokens.json is a machine assumption that
    // will not survive being shipped.
    [Trait("Category", "Unit")]
    [Fact]
    public void NoContentFile_ContainsAnAbsoluteDrivePath()
    {
        var offenders = new List<string>();
        var root = WorkloadsRoot();

        foreach (var f in ContentFiles())
        {
            // Walk PARSED strings, never the raw text. Scanning raw JSON matches escape
            // sequences - "results:\nfoo" looks like a drive path "s:\n" to any naive
            // pattern - which produced a screen of false positives on the first run, the
            // same class of artifact that put two fictional rows in this campaign's
            // original census.
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(f)); }
            catch (JsonException) { continue; }   // unparsable content is the hygiene campaign's problem
            using (doc)
            {
                foreach (var s in Strings(doc.RootElement))
                {
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(s, DrivePathPattern))
                    {
                        // Report the MATCH WITH CONTEXT, never the head of the string. An
                        // earlier version printed the first 70 characters, which for a
                        // multi-kilobyte JavaScript blob named the file and told you
                        // nothing about why - a guard whose failure message cannot be
                        // acted on is barely better than no guard.
                        var from = Math.Max(0, m.Index - 18);
                        var len = Math.Min(46, s.Length - from);
                        var context = s.Substring(from, len);

                        // EXEMPT, deliberately and by name: an installed-application
                        // location is identical on every Windows machine that has the
                        // application, so tokenizing it buys ceremony and no portability.
                        if (context.Contains("Program Files", StringComparison.OrdinalIgnoreCase)) continue;

                        // A URL is not a drive path. "http://x" contains "p:/" - the same
                        // artifact that put two fictional rows in this campaign's census.
                        if (context.Contains("://", StringComparison.Ordinal)) continue;

                        offenders.Add($"{Path.GetRelativePath(root, f)}: ...{context}...");
                        break;
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "absolute drive paths must live only in tokens.json; found in:\n  " +
            string.Join("\n  ", offenders.Take(20)));
    }

    // A token nothing can resolve is worse than a literal: the literal at least fails
    // where you can see it. This is the check `canary doctor` will run before a suite.
    [Trait("Category", "Unit")]
    [Fact]
    public void EveryTokenInTheCorpus_Resolves()
    {
        CanaryTokens.Invalidate();
        var root = WorkloadsRoot();
        var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in ContentFiles())
        {
            foreach (var name in CanaryTokens.FindUnresolved(File.ReadAllText(f), root))
                unresolved.Add(name);
        }

        Assert.True(unresolved.Count == 0,
            "unresolvable tokens in the corpus: " + string.Join(", ", unresolved));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TokensFile_IsValidAndDeclaresEveryRootTheCorpusUses()
    {
        var root = WorkloadsRoot();
        Assert.Null(CanaryTokens.DescribeProblem(root));

        CanaryTokens.Invalidate();
        var map = CanaryTokens.Load(root);
        Assert.NotEmpty(map);
        Assert.True(map.ContainsKey("CANARY_REPO_BRISTLE"), "the largest single dependency must be declared");
    }

    // Proves the whole chain on real content: a shipped test file, expanded through the
    // shipped token table, yields a path that exists on this machine.
    [Trait("Category", "Unit")]
    [Fact]
    public void AConvertedTestFile_ExpandsToARealLocation()
    {
        CanaryTokens.Invalidate();
        var root = WorkloadsRoot();

        var file = ContentFiles().FirstOrDefault(f => File.ReadAllText(f).Contains("%CANARY_REPO_BRISTLE%"));
        Assert.NotNull(file);

        using var doc = JsonDocument.Parse(File.ReadAllText(file!));
        var raw = doc.RootElement.ToString();
        Assert.Contains("%CANARY_REPO_BRISTLE%", raw);

        var expanded = CanaryTokens.Expand(raw, root);
        Assert.DoesNotContain("%CANARY_REPO_BRISTLE%", expanded);
        Assert.Contains("Bristle", expanded);

        // and the root it resolves to is a directory that actually exists here
        var bristle = CanaryTokens.Expand("%CANARY_REPO_BRISTLE%", root);
        Assert.True(Directory.Exists(bristle), $"token resolved to {bristle}, which does not exist");
    }
}
