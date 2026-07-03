using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Canary.Tests.Contracts;

// R1.4 (2026-07-03) — drift lock for the PINNED GetFrameState reflection contract.
//
// RhinoAgent reads Penumbra.Bridge.FrameState fields BY NAME via reflection (the pinned seam,
// audit-c). A rename/removal on Penumbra's side produces NO compile error anywhere and breaks
// every rhino-workload run at the next launch. These tests fail FIRST:
//
//  1. The pinned field set (mirroring spec/PEERS.md § GetFrameState pin) must exist verbatim
//     in Penumbra's FrameState class — source-parsed from the SIBLING CHECKOUT at
//     C:\Repos\Penumbra (operator-run local test per amended decision #8; SKIPS when the
//     sibling checkout is absent, e.g. a Canary-only clone).
//  2. RhinoAgent must not grow a GetField("...") read outside the pinned set — additions to
//     the contract go: Penumbra field first (additive), then PEERS pin, then THIS list, then
//     the RhinoAgent read (null-tolerant for older plugins).
[Trait("Category", "Unit")]
public class FrameStateContractTests
{
    // Mirrors Penumbra/spec/PEERS.md § "GetFrameState() — the PINNED reflection contract".
    private static readonly string[] PinnedFields =
    {
        "PresentedRevision",
        "RealRevision",
        "EvalMode",
        "Status",
        "DisabledByError",
        "LastFramePath",
        "BakesOutstanding",   // additive 2026-07-03 (bug 0058 / R1.2) — null-tolerant read
    };

    private static readonly string PenumbraBridgeSource =
        @"C:\Repos\Penumbra\hosts\rhino\Penumbra.Bridge\PenumbraBridge.cs";

    [Fact]
    public void PinnedFields_AllExistInPenumbraFrameState()
    {
        // Skip ONLY when the sibling checkout is entirely absent (Canary-only clone) —
        // environment-dependent local gate per amended decision #8. If the file EXISTS but the
        // FrameState block can't be located, that is a LOUD FAILURE, not a skip: the contract
        // anchor moved (rename/relocation/reformat) and silence here would be indistinguishable
        // from a pass (adversarial-review catch, 2026-07-03).
        if (!File.Exists(PenumbraBridgeSource))
            return;

        var src = File.ReadAllText(PenumbraBridgeSource);
        // Body = declaration to the first closing brace on its own line. Indentation-agnostic
        // (\s*): FrameState is a flat field bag with no nested blocks, so the first own-line
        // brace IS the class end regardless of nesting depth.
        var m = Regex.Match(src, @"class\s+FrameState\s*\{(?<body>[\s\S]*?)\n\s*\}");
        Assert.True(m.Success,
            $"Penumbra.Bridge source exists at {PenumbraBridgeSource} but the FrameState class " +
            "block could not be located — the pinned contract anchor moved (renamed? relocated?). " +
            "Reconcile per Penumbra/spec/PEERS.md § GetFrameState pin, then update this test's parser.");
        var body = m.Groups["body"].Value;

        var missing = PinnedFields
            .Where(f => !Regex.IsMatch(body, $@"public\s+\w+(\[\])?\s+{Regex.Escape(f)}\s*;"))
            .ToArray();
        Assert.True(missing.Length == 0,
            "PINNED FrameState field(s) missing from Penumbra.Bridge (rename/removal breaks the " +
            "reflection seam silently): " + string.Join(", ", missing) +
            ". Coordinate per Penumbra/spec/PEERS.md § GetFrameState pin.");
    }

    [Fact]
    public void RhinoAgent_ReadsOnlyPinnedFields()
    {
        // RhinoAgent.cs ships in this repo — always available.
        var agentPath = Path.Combine(FindRepoRoot(), "src", "Canary.Agent.Rhino", "RhinoAgent.cs");
        var src = File.ReadAllText(agentPath);
        var reads = Regex.Matches(src, "GetField\\(\"(?<name>[A-Za-z0-9_]+)\"\\)")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(reads);
        var unpinned = reads.Except(PinnedFields).ToArray();
        Assert.True(unpinned.Length == 0,
            "RhinoAgent reads FrameState field(s) outside the pinned contract set: " +
            string.Join(", ", unpinned) +
            ". Add to Penumbra first (additive), then the PEERS pin, then PinnedFields here.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("Canary.sln not found above test bin dir");
        return dir.FullName;
    }
}
