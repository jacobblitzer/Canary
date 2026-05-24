---
date: 2026-05-24
tags: [research, canary, prior-art, debug-overhaul]
status: completed
project: canary
component: full-surface
---

# Test-harness prior-art survey

Parent prompt: `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md` (Phase B).
Sibling: `2026-05-24-canary-surface-audit.md` (Phase A).
Drives: `docs/plans/2026-05-24-canary-debug-overhaul.md` (Phase C).

This is **not** a literature review — it's "what conventions exist that we'd be foolish to reinvent." Three references. Per reference: ~150 words on what they do, what's relevant to Canary's asks, what to steal, what to skip.

## Third-tool selection

Required: Playwright Inspector + Trace Viewer, Cypress dashboard.

Third pick: **Sysinternals Process Explorer.** Playwright + Cypress already cover the test-runner-UI angle deeply (live execution, time travel, screenshot review, retry). The asks Canary needs help with that neither tool addresses are the **localhost manager** (§C7) and the **process-tree provenance** (Tier 1–3): "which dev server is on port 5173, what spawned it, when, kill it." Process Explorer is the canonical reference for that surface — process tree, per-row kill/suspend/properties, command-line column, search across all processes. Picking a second test-runner tool (Vitest UI, Selenium IDE) would have been redundant with Playwright + Cypress; picking the desktop-telemetry angle complements them.

---

## Reference 1 — Playwright Inspector + Trace Viewer

### What it does
Two related tools shipped with Microsoft Playwright. **Inspector** is a live-execution overlay: a sidebar attached to the test-runner window that lets the operator step through a test action-by-action, see the locator and call being made, pause/resume, and inspect the current page state. Activated by `PWDEBUG=1` or `--debug`. **Trace Viewer** is post-hoc: every test run can be recorded into a `.zip` trace containing per-action DOM snapshots, console logs, network requests, screenshots before/after each action, source-file pointers, and a timeline scrubber. Loaded via `npx playwright show-trace trace.zip` (or in CI artifacts). The operator drags the timeline cursor; the viewer reconstructs the page state, console output, and network panel for that exact moment.

### Relevance to Canary's asks
- **Ask #2 (universal telemetry):** Trace Viewer is the canonical example of "console + network + screenshots + action log, all aligned on a timeline, queryable after the fact." Its data shape is essentially the universal envelope §C1 needs.
- **Ask #4 (non-headless):** Inspector is explicitly the "watch tests run live with the browser visible" surface. Playwright defaults to headless; Inspector flips it.
- **Ask #8 (live + past-results):** Inspector handles live; Trace Viewer handles past — same data model, two consumers.

### Steal
- The **`.trace` zip-bundle format.** One file per run, self-contained, viewable offline. Drop it into `workloads/<w>/results/<test>/runs/<timestamp>/canary-trace.zip` and Claude can ingest it without scraping five paths.
- The **action / network / console / DOM-snapshot triplet** aligned on a single timeline. This IS the universal telemetry envelope shape — Canary's per-checkpoint card flow is a coarser version of the same idea.
- **Timeline scrubber.** Drag-to-replay-state is more debuggable than "list of log lines."

### Skip
- The full DOM-snapshot replay engine is overkill for Canary v1 — Penumbra's canvas is GPU-rendered (not reconstructable from DOM); Rhino's viewport is native (no DOM at all). Per-checkpoint static screenshots + console/network logs aligned on the timeline get 80% of the value at 5% of the implementation cost.
- The Inspector pause/step workflow needs Playwright's dispatcher architecture — Canary's named-pipe agents don't pause mid-action. Out of scope for v1.

---

## Reference 2 — Cypress (App + Cloud Dashboard)

### What it does
Cypress runs end-to-end browser tests in a dedicated desktop runner: a chromeless Chromium window with a **command log sidebar** showing every test command as it executes (`.click()`, `.should()`, `.contains()`, etc.) plus pass/fail state per command. The operator points at any command in the log → Cypress shows the DOM state at that exact moment ("time travel"). The **Cloud Dashboard** (paid, opt-in) aggregates runs across CI machines: per-test history, screenshot/video archives, parallelization metadata, GitHub PR status checks. Both surfaces share the same data shape: test → spec → command → assertion. Cypress is also famously opinionated about visible runs: `cypress open` (interactive, visible) and `cypress run` (headless, CI) are explicit verbs, with `open` as the default first-experience.

### Relevance to Canary's asks
- **Ask #4 (non-headless):** Cypress is the canonical reference for "visible runner first; headless is a separate verb." `cypress open` is exactly what `canary run` should become per §STANDARD.md §16 rule 8.
- **Ask #6 + #8 (UI overhaul + live):** The command-log + DOM-time-travel pane is a direct analog for Canary's TestRunnerPanel + ProgressFeedPanel. Cypress's split (command log + DOM viewport + Chrome devtools) maps cleanly onto Canary's (log + progress feed + future telemetry panel).
- **Ask #9 (localhost manager):** Cypress doesn't help here directly, but its handling of "tests in a browser the runner spawned" is exactly the spawn-registry pattern §C7 Tier 2 needs.

### Steal
- **`open` vs `run` verb naming.** Renaming Canary's flag is a small change with outsized clarity (`canary open` = visible, `canary run` = headless/CI) — though preserving `canary run` for backward compatibility while reversing its default is also fine.
- **Per-command status icons in the live log.** Cypress's green checkmark / red X next to each command-as-it-fires is more useful than Canary's text-only log.
- **Time-travel-by-clicking-the-log-line.** Click "checkpoint X" in the run log → screenshot + telemetry for that moment. Maps directly onto the Past Runs panel (§C8).

### Skip
- The Cypress Cloud Dashboard is a SaaS product. Canary's data should stay local-first. The on-disk format (per-run dir, screenshot + video) is fine; the cloud aggregation is not Canary's job.
- Cypress's command-DSL (`cy.click().should()`) is a custom test language. Canary's test JSON works — don't replace it with a DSL.

---

## Reference 3 — Sysinternals Process Explorer

### What it does
Microsoft's canonical Windows process-tree tool (Mark Russinovich, ~2 decades old, ships free with Sysinternals Suite). Surface: a **tree view of all processes** rooted at System, with columns for PID, CPU%, working set, command line, owner, integrity level, and (with an option toggled) the listening ports. Per-row context menu: **End Process / End Process Tree / Suspend / Properties / Search Online / Set Affinity / Restart**. Search bar (Ctrl+F) finds processes by handle, DLL, or string. Live updating; visual highlighting for processes that started/exited within the last second (green/red flash). Hovering a process reveals its file-version signature info. Run-as-admin unlocks kernel-level details.

### Relevance to Canary's asks
- **Ask #9 (localhost manager):** Direct match. The Tier 1 "passive dev-port enumeration" + Tier 3 "name-heuristic process listing" UI is essentially a stripped-down Process Explorer scoped to dev-server-relevant processes. The per-row Kill / Restart action is exactly the same pattern.
- **Ask #2 (telemetry, indirectly):** Process Explorer's "show me the command line that started this process" column is a small but load-bearing UX trick — operators identify orphaned dev servers by their `npm run dev` args, not their PIDs. The Tier 2 spawn registry should capture command line, not just PID.
- **Ask #6 (UI overhaul):** Treating process tree + ports as a built-in panel (not a separate tool) — Process Explorer's lesson is that telemetry tools win when they're one click away, not one app-switch away.

### Steal
- **Per-row "End Process Tree" semantics.** Killing the parent without killing children leaves orphaned Vite servers (the exact failure mode `ViteManager.KillStaleListenerAsync` was added to fix). The localhost manager's Kill action should default to tree-kill, mirroring `taskkill /F /T`.
- **Command-line column.** Show the operator the `npm run dev -- --port 3000 --strictPort` string, not just `node.exe PID 12345`. Canary already has this data via the spawn registry (§C7 Tier 2).
- **Live update with flash on state change.** When a Vite server exits unexpectedly mid-test, the localhost manager row should flash + tooltip the exit code rather than silently disappear.

### Skip
- Kernel-handle inspection, DLL listing, signer info — way out of scope for Canary. The localhost manager is a tiny slice of Process Explorer's scope: dev-server ports + Canary-spawned children + name-heuristic JS/dotnet/python processes. No need to grow toward general-purpose process management.
- Search-the-internet for unknown processes (the "Search Online" item) — security-context feature, not useful for Canary's dev-loop scope.

---

## Cross-tool synthesis (for Phase C)

| Convention | Source | Used in Canary §C |
|---|---|---|
| Console + network + screenshot + action triplet, one timeline | Playwright Trace Viewer | C1 telemetry envelope, C2 REPORT.md |
| Self-contained run artifact (zip / dir) | Playwright | C2 — `workloads/<w>/.../runs/<timestamp>/` |
| Visible-runner-as-default-verb | Cypress `open` vs `run` | C3 non-headless enforcement |
| Per-command status icons in live log | Cypress | C4 UI overhaul, C8 live panel |
| Click-the-log-line → time-travel | Cypress | C8 past-results browser |
| Process tree with command-line column | Process Explorer | C7 Tier 1 + Tier 2 |
| End-process-tree (not just process) default | Process Explorer | C7 Kill action |
| Live updates with state-change flash | Process Explorer | C7 |

---

## Sources

- Playwright Inspector + Trace Viewer: `playwright.dev/docs/debug` and `playwright.dev/docs/trace-viewer` (public docs, periodically referenced for behavior; recalled from knowledge cutoff 2026-01).
- Cypress App + Cloud: `docs.cypress.io` (open-source app + paid Cloud product; `cypress open` vs `cypress run` verb model is in the Cypress CLI docs).
- Sysinternals Process Explorer: `learn.microsoft.com/sysinternals/downloads/process-explorer` (Microsoft-hosted; long-running canonical tool, no version churn relevant to this survey).

No live web fetches were required for this survey — the conventions cited are stable, long-documented, and recalled from training. If Phase C surfaces a question requiring fact-check on a specific feature (e.g., exact Trace Viewer file extension or Cypress Cloud free-tier limits), validate then.
