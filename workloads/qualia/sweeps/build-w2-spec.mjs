#!/usr/bin/env node
/**
 * build-w2-spec.mjs — generate the W2 OFAT atlas spec from Qualia source.
 *
 * Usage: node workloads/qualia/sweeps/build-w2-spec.mjs [--qualia <dir>]
 *
 * Enumerates the REAL lever inventory (Discipline 7 — ground at source,
 * regenerate rather than trust stamped counts):
 *   - persona ids from packages/core/src/modules/builtin.ts
 *   - perf field names from packages/renderer/src/perfSettingsDefaults.ts
 *     (mutated as kind 'perfAuto' — the driver derives an alternate value
 *     from the BASE fingerprint at runtime: booleans flip, numbers scale,
 *     enums come from the alternates map below, unknown strings skip)
 * plus explicit junction presets, theme, and profile round-trips (the
 * to-self profile is a deliberate CONTROL state: expected 0 effect/0 leak).
 *
 * Families: ddv x {minimal,standard,workshop,cinematic} +
 * workshop-palette x {minimal,standard} + qnode-junction-alignment-test x
 * {minimal}. compute.layout is force-disabled in every family base
 * (verified ground rule: layout bases never settle; the layout ticker
 * itself is still swept as a persona mutation).
 */

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const qIdx = args.indexOf('--qualia');
const QUALIA = qIdx >= 0 ? args[qIdx + 1] : 'C:/Repos/Qualia';

const builtinSrc = fs.readFileSync(path.join(QUALIA, 'packages/core/src/modules/builtin.ts'), 'utf8');
const personaIds = [...builtinSrc.matchAll(/id: '([a-z0-9.-]+)'/g)].map((m) => m[1]);

const defaultsSrc = fs.readFileSync(path.join(QUALIA, 'packages/renderer/src/perfSettingsDefaults.ts'), 'utf8');
const bodyStart = defaultsSrc.indexOf('return {');
const perfFields = [...defaultsSrc.slice(bodyStart).matchAll(/^\s{4}([a-zA-Z][a-zA-Z0-9]*):/gm)].map((m) => m[1]);

if (personaIds.length < 50 || perfFields.length < 60) {
  console.error(`Extraction looks wrong: ${personaIds.length} personas, ${perfFields.length} perf fields`);
  process.exit(1);
}

// String-enum alternates — OBSERVED values only (a guessed-invalid enum
// would read as a fake no-op). Unknown string fields are skipped by the
// driver and reported as skipped, not as findings.
const alternates = {
  nodeHaloVariant: ['aurora-vent', 'soft-glow', 'none'],
  socketVariant: ['solder', 'bracket', 'none'],
  nubVariant: ['shaded-sphere', 'ink-dot', 'none'],
  edgeShape: ['catmull-rom', 'straight'],
  edgeGradientRampId: ['bioluminescent'],
  renderMode: ['particulate', 'surface'],
};

const EXCLUDE_PERF = new Set(['activeJunction']); // covered by explicit junction mutations

const mutations = [
  ...personaIds.map((id) => ({ id: `persona-${id.replace(/[^a-z0-9-]/g, '-')}`, kind: 'persona', persona: id })),
  ...perfFields.filter((f) => !EXCLUDE_PERF.has(f)).map((f) => ({ id: `perf-${f}`, kind: 'perfAuto', field: f })),
  ...['bubble', 'center', 'surface', 'pull-back', 'voronoi'].map((p) => ({ id: `junction-${p}`, kind: 'junction', preset: p })),
  { id: 'theme-light', kind: 'theme', value: 'light' },
  ...['minimal', 'standard', 'workshop', 'cinematic'].map((p) => ({ id: `profile-${p}`, kind: 'profile', to: p })),
];

const spec = {
  name: 'w2-atlas',
  chunkSize: 6,
  disablePersonas: ['compute.layout'],
  alternates,
  families: [
    { base: 'minimal', fixture: { kind: 'ddv' } },
    { base: 'standard', fixture: { kind: 'ddv' } },
    { base: 'workshop', fixture: { kind: 'ddv' } },
    { base: 'cinematic', fixture: { kind: 'ddv' } },
    { base: 'minimal', fixture: { kind: 'demo', slug: 'workshop-palette' } },
    { base: 'standard', fixture: { kind: 'demo', slug: 'workshop-palette' } },
    { base: 'minimal', fixture: { kind: 'demo', slug: 'qnode-junction-alignment-test' } },
  ],
  mutations,
};

const out = path.join(HERE, 'specs', 'w2-atlas.json');
fs.writeFileSync(out, JSON.stringify(spec, null, 2) + '\n');
console.log(`w2-atlas spec: ${personaIds.length} personas + ${perfFields.length - EXCLUDE_PERF.size} perfAuto + 10 explicit = ${mutations.length} mutations x ${spec.families.length} families = ${mutations.length * spec.families.length} states -> ${out}`);
