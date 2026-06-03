---
type: bugs
repo: Canary
open_bugs: null
total_bugs: 8
last_updated: 2026-06-02
priority_max: null
bugs_dir: docs/bugs/
---

> **2026-06-02 — BUG-0007** RESOLVED (resolved-date 2026-06-02): RunCommand had already been fixed (Task<int> + ctx.ExitCode) at some point between the original filing and today, but the bug doc was never updated. Audit found three sibling commands using the same broken void-handler pattern (ApproveCommand, ReportCommand, RecordCommand) — all now use the `ctx => { ctx.ExitCode = … }` pattern via an internal `*Inner` method. Verified end-to-end: `canary run / approve / report` all propagate `1` on error and `0` on success. See [docs/bugs/0007-cli-exit-code-regression.md](docs/bugs/0007-cli-exit-code-regression.md).
>
> **2026-06-02 — BUG-0009** RESOLVED-NOT-CANARY (Canary `59456b9`): the "extreme close-up" framing was actually extreme zoom-OUT caused by upstream CPig FD divergence (un-tuned torques on closed-loop linkages → bodies fly to (millions, millions, 0) mm → ZoomBoundingBox correctly framed the huge bbox → mechanism became a single-pixel speck). Solo and shared modes produce identical bbox+camera per new diagnostic logging in `HandleSetViewport` (writes to `C:/Repos/CPig/logs/agent_viewport_diag.log`). Self-resolved when CPig torque-tuning commits `f7506b6` + `c091570` bounded motion. See [docs/bugs/0009-shared-session-framing-regression.md](docs/bugs/0009-shared-session-framing-regression.md).

# Bugs

Per-bug notes live in `docs/bugs/<NNNN-slug>.md`. This file is a dashboard-frontmatter pointer — the canonical list is the `docs/bugs/` folder.

> **TODO:** `open_bugs` is null because computing it requires reading the
> `status:` frontmatter on each `docs/bugs/*.md` file. Generating that
> aggregate is on the dashboard backlog. `total_bugs` reflects the file
> count at the last audit.
