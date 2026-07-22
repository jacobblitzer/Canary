#!/usr/bin/env node
/**
 * generate-sweep.mjs — emit Canary micro-tests + a suite from a sweep spec.
 *
 * Usage:
 *   node workloads/qualia/sweeps/generate-sweep.mjs <spec.json> [--run-id <id>]
 *
 * Display-sweep W1 (2026-07-19). Campaign:
 * Qualia/docs/plans/2026-07-19-display-behavior-sweep.md · verified ground
 * rules in MultiVerse/prompts/qualia-display-sweep-2026-07-19.md.
 *
 * Shape (per the adversarial verification):
 *   - ONE `canary run --suite sweep-<name>` boots Vite+Chrome once; every
 *     generated test reuses that boot (per-test cost ~1-3s).
 *   - One test per FAMILY (base x fixture). Actions are CHUNKED
 *     RunCommands — each chunk runs `chunkSize` states and stays far
 *     under the agent's 60s CDP evaluate ceiling; a crashed action loses
 *     only its own test's remaining chunks, not the suite.
 *   - The in-page driver (sweep-driver.js) is embedded via JSON.stringify
 *     as a setup command, so tests are self-contained strict JSON.
 *   - Observations land at Qualia/debug-logs/<sweepId>/ via the dev
 *     server's POST /api/debug/write (telemetry console lines are backup
 *     only — the NDJSON sink truncates >500KB lines destructively).
 *
 * Spec shape:
 * {
 *   "name": "w1-smoke",
 *   "fixture": { "kind": "ddv" } | { "kind": "demo", "slug": "workshop-palette" },
 *   "bases": ["minimal"],
 *   "chunkSize": 4,
 *   "mutations": [
 *     { "id": "persona-film-grain", "kind": "persona", "id2": null, "idPersona": "fx.film-grain" }, // see normalize()
 *     { "id": "perf-edge-width", "kind": "perf", "field": "edgeWidthPx", "value": 6 },
 *     { "id": "junction-bubble", "kind": "junction", "preset": "bubble" },
 *     { "id": "theme-light", "kind": "theme", "value": "light" },
 *     { "id": "profile-workshop", "kind": "profile", "to": "workshop" }
 *   ]
 * }
 * Mutation `id` is the state id (kebab, unique). Persona mutations use
 * `persona` for the persona id (aliases: idPersona).
 */

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const QUALIA_DIR = path.resolve(HERE, '..');           // workloads/qualia

const args = process.argv.slice(2);
const specPath = args.find((a) => !a.startsWith('--'));
const runIdIdx = args.indexOf('--run-id');
const runId = (runIdIdx >= 0 ? args[runIdIdx + 1] : new Date().toISOString().slice(0, 16).replace(/[-:T]/g, ''))
  .replace(/[^a-zA-Z0-9_-]/g, '_');
// Platform-foundation P1 (2026-07-22): --workload <name> emits the
// generated tests/suite into workloads/<name>/ instead of qualia/ —
// the desktop leg generates its own sweep suites (fresh driver embed)
// so web regeneration can never silently mutate desktop tests.
const workloadIdx = args.indexOf('--workload');
const targetWorkload = workloadIdx >= 0 ? args[workloadIdx + 1] : 'qualia';
const TARGET_DIR = path.resolve(QUALIA_DIR, '..', targetWorkload);
const TESTS_DIR = path.join(TARGET_DIR, 'tests');
const SUITES_DIR = path.join(TARGET_DIR, 'suites');
const RUNS_DIR = path.join(HERE, 'runs');   // manifests stay centralized with the toolchain

if (!specPath) {
  console.error('Usage: node generate-sweep.mjs <spec.json> [--run-id <id>] [--workload <name>]');
  process.exit(2);
}
if (!fs.existsSync(TARGET_DIR)) {
  console.error(`workload dir does not exist: ${TARGET_DIR}`);
  process.exit(2);
}
fs.mkdirSync(TESTS_DIR, { recursive: true });
fs.mkdirSync(SUITES_DIR, { recursive: true });

const spec = JSON.parse(fs.readFileSync(specPath, 'utf8'));
const driverSrc = fs.readFileSync(path.join(HERE, 'sweep-driver.js'), 'utf8');
const sweepId = `${spec.name}-${runId}`.replace(/[^a-zA-Z0-9_-]/g, '_');
const chunkSize = spec.chunkSize ?? 4;

const normalize = (m, i) => {
  const id = (m.id ?? `m${i}`).replace(/[^a-zA-Z0-9_-]/g, '-');
  const mut = { ...m };
  delete mut.id;
  if (mut.kind === 'persona') mut.id = mut.persona ?? mut.idPersona;
  return { id, mutation: mut };
};
const states = (spec.mutations ?? []).map(normalize);
// W3 pairs: each {id, a, b} expands to three adjacent states — the two
// singles (measured in-run so union math shares the exact base) + the
// pair. The deriver's interaction section keys off the `--ab` suffix.
for (const p of spec.pairs ?? []) {
  const id = p.id.replace(/[^a-zA-Z0-9_-]/g, '-');
  const a = normalize({ ...p.a, id: `${id}--a` });
  const b = normalize({ ...p.b, id: `${id}--b` });
  states.push(a, b, {
    id: `${id}--ab`,
    mutation: { kind: 'pair', a: a.mutation, b: b.mutation },
  });
}
const dupes = states.map((s) => s.id).filter((id, i, a) => a.indexOf(id) !== i);
if (dupes.length) { console.error('Duplicate state ids: ' + dupes.join(', ')); process.exit(2); }

// Families: explicit spec.families [{base, fixture}] wins; else the
// legacy bases x single-fixture cross.
const families = spec.families
  ?? spec.bases.map((base) => ({ base, fixture: spec.fixture }));

const testNames = [];
for (const { base, fixture, enterContext } of families) {
  const family = `${base}-${fixture.kind === 'demo' ? fixture.slug : fixture.kind}`
    + (enterContext ? `-in-${enterContext.replace(/[^a-zA-Z0-9_-]/g, '_')}` : '');
  const testName = `sweep-${spec.name}-${family}`;
  const ctx = {
    sweepId, family, base, fixture, sweepName: spec.name,
    // W2 content-arming: family init switches into this context after
    // fixture load (content-gated personas need an entered qverse /
    // nested contexts / cross-context edges in view).
    enterContext: enterContext ?? null,
    alternates: spec.alternates ?? {},
    disablePersonas: spec.disablePersonas ?? [],
  };

  const actions = [];
  for (let i = 0; i < states.length; i += chunkSize) {
    const chunk = states.slice(i, i + chunkSize);
    actions.push({
      type: 'RunCommand',
      command: `window.__sweep.runChunk(${JSON.stringify(chunk)}, ${Math.floor(i / chunkSize)})`,
    });
  }
  if (spec.planarWalk) {
    actions.push({ type: 'RunCommand', command: 'window.__sweep.runPlanarWalk()' });
  }

  const test = {
    name: testName,
    workload: targetWorkload,
    description:
      `Display-sweep family ${family} (sweep ${sweepId}): ${states.length} one-lever states from base '${base}' on the ` +
      `${fixture.kind === 'demo' ? fixture.slug : 'DDV'} fixture. Structural fingerprints only; observations ` +
      `land in Qualia/debug-logs/${sweepId}/. Generated by workloads/qualia/sweeps/generate-sweep.mjs — do not hand-edit.`,
    setup: {
      canvas: { width: 1280, height: 720 },
      commands: [
        'window.__canaryWaitForReady(30000)',
        'window.__canaryCloseLandingScreen()',
        driverSrc,
        `window.__sweep.init(${JSON.stringify(ctx)})`,
      ],
    },
    actions,
    checkpoints: [],
  };

  fs.writeFileSync(path.join(TESTS_DIR, `${testName}.json`), JSON.stringify(test, null, 2) + '\n');
  testNames.push(testName);
}

const suiteName = `sweep-${spec.name}`;
fs.writeFileSync(path.join(SUITES_DIR, `${suiteName}.json`), JSON.stringify({
  name: suiteName,
  description:
    `Generated display-sweep suite (sweep ${sweepId}) — ${testNames.length} family test(s) x ${states.length} states, ` +
    'structural assertions via in-page driver, no pixel checkpoints. Regenerate via workloads/qualia/sweeps/generate-sweep.mjs.',
  tests: testNames,
}, null, 2) + '\n');

fs.mkdirSync(RUNS_DIR, { recursive: true });
fs.writeFileSync(path.join(RUNS_DIR, `${sweepId}.json`), JSON.stringify({
  sweepId, generatedAt: new Date().toISOString(), spec, suiteName, testNames,
  workload: targetWorkload,
  expectedObs: { perFamily: states.length + 1, families: families.length },
  // Both legs land here: the dev middleware writes to <Qualia cwd>/debug-logs,
  // and the desktop leg's debug_write_file IPC does the same because Canary
  // launches the exe with cwd = the Qualia repo.
  observationsDir: `C:/Repos/Qualia/debug-logs/${sweepId}`,
}, null, 2) + '\n');

console.log(`sweep ${sweepId}: ${testNames.length} test(s) -> ${TESTS_DIR}`);
console.log(`suite ${suiteName} -> ${SUITES_DIR}`);
console.log(`run:  .\\canary.cmd run --workload ${targetWorkload} --suite ${suiteName} --headless`);
console.log(`then: node workloads/qualia/sweeps/derive.mjs ${sweepId}`);
