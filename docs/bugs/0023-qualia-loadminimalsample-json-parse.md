---
title: __canaryLoadMinimalSample fails to parse the sample; the 11 rh2 tests render the wrong scene
status: open
severity: medium
found: 2026-08-18
area: qualia workload
---

# 0023 — `__canaryLoadMinimalSample()` fails; rh2 tests capture the wrong scene

## Symptom

Every one of the 11 `rh2-*` tests logs, during setup:

```
window.__canaryLoadMinimalSample()
  → {"ok":false,"reason":"Unexpected non-whitespace character after JSON at position 7673 (line 297 column 1)"}
```

The suite reports **11 passed**. It passes because every checkpoint is `mode: "capture"`,
which is save-only and never FAILs. **A real failure inside the test produced a green
result.** That is the capture-mode trade-off working as designed, and it is worth stating
plainly: capture proves an image was taken, nothing more.

## What this means

The tests set their perf snapshot and capture successfully — the settings genuinely take
effect, as 11 captures across 6 distinct image hashes shows — but they are rendering
**whatever the app booted with**, not the minimal sample they intend to measure. So the
images are real and the settings are real, but the scene is not the declared one.

## What has been established

- The repo's `examples/demos/minimal.qualia` is 7971 bytes / 296 lines. Its JSON value ends
  at char 7965 with only `\r\n` after it, and it parses cleanly.
- The Vite dev server **serves it correctly**: fetched over HTTP it is 7971 bytes and parses
  as JSON.
- `examples/minimal/.qualia` (a different file, 7676 bytes / 296 lines, JSON ending at 7671
  with one `\n` after) also parses cleanly. Its size is suspiciously close to the reported
  failure offset of 7673, but that is a coincidence worth testing rather than a conclusion.
- The hook fetches `/examples/demos/${slug}.qualia` and then hands the body to
  `importGraph` from `@qualia/core`.
- Blast radius is exactly these 11 tests — they are the only qualia tests that call
  `__canaryLoadMinimalSample`.

## What has NOT been established

Why the browser's parse fails at 7673 when both candidate files parse and the server serves
valid JSON. The remaining possibilities need devtools rather than inference:

1. `importGraph` parses something other than the whole body (a nested field, a second
   document), and the reported offset refers to that.
2. The hook's SPA-fallback body-sniffing path mangles the content before parsing.
3. A BOM or line-ending transform between `fetch().text()` and `JSON.parse`.

## Next step

Run the qualia dev server, open the page, and call `window.__canaryLoadMinimalSample()` from
the console with a breakpoint in `importGraph`. The offset will identify the string actually
being parsed in one step.

## Not caused by the rh2 repair

The rh2 tests were unparseable JSON from their creation on 2026-05-14 until 2026-08-18 and
had never executed. This defect was invisible until they ran for the first time — it is a
pre-existing fault in the sample-loading path that nothing had exercised.
