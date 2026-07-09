# Canary asks queue

This directory tracks work Canary needs from peer repos. Each ask is one Markdown file under a per-peer subdirectory.

## When to file an ask
- Canary needs a new hook from a consumer (e.g. Penumbra needs `__canaryPersonaX`).
- A baseline regeneration is needed because a deliberate visual change landed peer-side.
- A contract field needs renaming (Canary side will need a coordinated change).
- Telemetry capture for a workload is incomplete and the peer team needs to expose something.

## File shape
Path: `docs/asks/<peer>/<NNNN>-<slug>.md` where `<NNNN>` is per-peer ordinal (next-highest existing + 1).

Frontmatter:

```yaml
---
id: <NNNN>
peer: cpig | penumbra | qualia | rhino | pigture | slop
status: open | in-progress | landed
requested: YYYY-MM-DD
landed: YYYY-MM-DD          # only when status: landed
prompt: MultiVerse/prompts/<instantiated-name>.md  # only when a prompt was spawned
severity: low | normal | high
tags: [hook, baseline, contract, telemetry, ...]
---
```

Body sections (recommended):
- **Context** — what Canary needs and why
- **Shape of the answer** — what file / function / artifact the peer should produce
- **What Canary will do once the peer lands it** — closes the loop
- **Open questions** — anything the peer team needs to decide

## Lifecycle
1. **Open.** Canary side (or an operator on Canary's behalf) creates `<NNNN>-slug.md`. Status: open.
2. **Spawn coordinated-work prompt.** Operator instantiates `MultiVerse/prompts/_template-canary-coordinated-work.md` parameterized by `(peer, ask-id)`. Saves the instantiated prompt as `MultiVerse/prompts/canary-ask-<peer>-<id>-YYYY-MM-DD.md`. Updates ask frontmatter: `status: in-progress`, `prompt: <instantiated-path>`.
3. **Land.** Operator runs the prompt in the peer repo's any AI coding agent. Work lands. Ask frontmatter flips: `status: landed`, `landed: YYYY-MM-DD`. Append-only `### Resolution` section in body.
4. **Archive (optional).** After 90 days, landed asks may move to `docs/asks/<peer>/_archive/YYYY/`.

## MCP query interface
`mcp__canary__list_consumer_asks(peer?, status?)` returns the queue. `mcp__canary__get_consumer_ask(id)` returns one full ask. See `canary-debug-overhaul-implement-2026-05-24.md` Phase 6 for implementation.

## Related
- `Canary/spec/PEERS.md` — canonical per-peer contracts (current state).
- `MultiVerse/prompts/multiverse-canary-coordination-pattern-2026-05-24.md` — the prompt that landed this convention.
