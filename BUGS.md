---
type: bugs
repo: Canary
open_bugs: null
total_bugs: 8
last_updated: 2026-06-02
priority_max: null
bugs_dir: docs/bugs/
---

> **2026-06-02 — BUG-0009** opened: per-checkpoint viewport framing collapses to extreme close-ups in shared-session suite runs (`cpig-kinematics`). Solo `--test` invocations frame correctly; `--suite` zooms to a single body / label. Suspect `HandleSetViewport`'s post-switch `ZoomBoundingBox` unioning over stale shared-session geometry. See [docs/bugs/0009-shared-session-framing-regression.md](docs/bugs/0009-shared-session-framing-regression.md).

# Bugs

Per-bug notes live in `docs/bugs/<NNNN-slug>.md`. This file is a dashboard-frontmatter pointer — the canonical list is the `docs/bugs/` folder.

> **TODO:** `open_bugs` is null because computing it requires reading the
> `status:` frontmatter on each `docs/bugs/*.md` file. Generating that
> aggregate is on the dashboard backlog. `total_bugs` reflects the file
> count at the last audit.
