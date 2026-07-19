#!/usr/bin/env node
/**
 * derive.mjs — turn sweep observations into the four campaign reports.
 *
 * Usage:
 *   node workloads/qualia/sweeps/derive.mjs <sweepId> [--obs <dir>]
 *
 * Reads Qualia/debug-logs/<sweepId>/ (base-*.json + obs-*.json written by
 * sweep-driver.js), diffs each state's fingerprints against its family
 * base, and writes to workloads/qualia/sweeps/runs/<sweepId>/:
 *
 *   effects.json     — raw per-state changed-path diffs
 *   effect-table.md  — mutation -> observable-delta summary per family
 *   findings.md      — no-op report, state-leak report (reverted != base),
 *                      settle failures, unstable double-reads, errors
 *
 * Comparator rules (campaign ground rule 8): floats compare with eps 1e-6;
 * the driver already whitelists DebugStats and captures no timestamps
 * inside fingerprints. `touched` is compared (a leak signal), as is the
 * persona profile (the 'custom'-collapse detector).
 */

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const sweepId = args.find((a) => !a.startsWith('--'));
if (!sweepId) { console.error('Usage: node derive.mjs <sweepId> [--obs <dir>]'); process.exit(2); }
const obsIdx = args.indexOf('--obs');
const manifestPath = path.join(HERE, 'runs', `${sweepId}.json`);
const manifest = fs.existsSync(manifestPath) ? JSON.parse(fs.readFileSync(manifestPath, 'utf8')) : null;
const obsDir = obsIdx >= 0 ? args[obsIdx + 1] : (manifest?.observationsDir ?? `C:/Repos/Qualia/debug-logs/${sweepId}`);
if (!fs.existsSync(obsDir)) { console.error(`Observations dir not found: ${obsDir}`); process.exit(1); }

const EPS = 1e-6;
const MAX_PATHS = 400;

/** Deep diff -> list of {path, a, b}. Arrays element-wise; numbers with eps. */
function diff(a, b, prefix = '', out = []) {
  if (out.length >= MAX_PATHS) return out;
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isFinite(a) && Number.isFinite(b) ? Math.abs(a - b) > EPS : a !== b) out.push({ path: prefix, a, b });
    return out;
  }
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
    if (JSON.stringify(a) !== JSON.stringify(b)) out.push({ path: prefix, a, b });
    return out;
  }
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  for (const k of keys) {
    diff(a[k], b[k], prefix ? `${prefix}.${k}` : k, out);
    if (out.length >= MAX_PATHS) break;
  }
  return out;
}

/** Group changed paths by top-level fingerprint section for readability. */
const sections = (paths) => {
  const bySection = {};
  for (const p of paths) {
    const top = p.path.split('.')[0];
    (bySection[top] ??= []).push(p);
  }
  return bySection;
};

const files = fs.readdirSync(obsDir);
const bases = new Map();   // family -> base record
const states = [];
const errors = [];
const skipped = [];
for (const f of files) {
  if (!f.endsWith('.json')) continue;
  const rec = JSON.parse(fs.readFileSync(path.join(obsDir, f), 'utf8'));
  if (rec.kind === 'base') bases.set(rec.family, rec);
  else if (rec.kind === 'state') states.push(rec);
  else if (rec.kind === 'skipped') skipped.push(rec);
  else if (rec.kind === 'error') errors.push(rec);
}
if (bases.size === 0) { console.error('No base-*.json records found — did the sweep init run?'); process.exit(1); }
states.sort((x, y) => x.seq - y.seq);

const effects = [];
for (const st of states) {
  const base = bases.get(st.family);
  if (!base) { errors.push({ kind: 'error', stateId: st.stateId, error: `no base record for family ${st.family}` }); continue; }
  const effect = diff(base.fp, st.fpMutated);
  const leak = diff(base.fp, st.fpReverted);
  effects.push({
    family: st.family, base: st.base, fixture: st.fixture, stateId: st.stateId, mutation: st.mutation, seq: st.seq,
    effectPaths: effect, leakPaths: leak,
    effectCount: effect.length, leakCount: leak.length,
    settleMutated: st.settleMutated, stableMutated: st.stableMutated, stableReverted: st.stableReverted,
    resetPreVerified: st.resetPre?.verified ?? null, resetPostVerified: st.resetPost?.verified ?? null,
  });
}

// ---- cross-family inconsistency mining ----
// Same mutation, different observable-effect SIGNATURE across families.
// Signatures normalize per-node paths (perNode.<id>. -> perNode.*.) and
// frame node keys so cross-fixture comparisons aren't just node-id noise;
// cross-BASE differences on the SAME fixture are the primary signal.
const normPath = (p) => p
  .replace(/perNode\.[^.]+\./, 'perNode.*.')
  .replace(/frame\.nodes\.[^.]+\./, 'frame.nodes.*.')
  .replace(/frame\.junctions\.[^.]+\./, 'frame.junctions.*.')
  .replace(/persona\.enabled\.[^.]+$/, 'persona.enabled.*');
const signature = (e) => [...new Set(e.effectPaths.map((p) => normPath(p.path)))].sort();
const byMutation = new Map();
for (const e of effects) (byMutation.get(e.stateId) ?? byMutation.set(e.stateId, []).get(e.stateId)).push(e);
const inconsistencies = [];
for (const [stateId, group] of byMutation) {
  if (group.length < 2) continue;
  const sigs = group.map((e) => ({ family: e.family, base: e.base, fixture: e.fixture?.kind === 'demo' ? e.fixture.slug : 'ddv', sig: signature(e), count: e.effectCount }));
  const uniq = new Set(sigs.map((s) => JSON.stringify(s.sig)));
  if (uniq.size <= 1) continue;
  // Prefer the cross-BASE-same-fixture comparison as the flag
  const byFixture = new Map();
  for (const s of sigs) (byFixture.get(s.fixture) ?? byFixture.set(s.fixture, []).get(s.fixture)).push(s);
  let crossBase = false;
  for (const [, fam] of byFixture) {
    if (new Set(fam.map((s) => JSON.stringify(s.sig))).size > 1) crossBase = true;
  }
  const all = new Set(sigs.flatMap((s) => s.sig));
  const perFamily = sigs.map((s) => ({
    family: s.family, count: s.count,
    missing: [...all].filter((p) => !s.sig.includes(p)).slice(0, 8),
  }));
  inconsistencies.push({ stateId, mutation: group[0].mutation, crossBase, perFamily });
}
inconsistencies.sort((a, b) => (b.crossBase ? 1 : 0) - (a.crossBase ? 1 : 0));

// Control states: profile-to-self must be a perfect no-op AND leak-free.
const controlViolations = effects.filter((e) =>
  e.mutation.kind === 'profile' && e.mutation.to === e.base && (e.effectCount > 0 || e.leakCount > 0));

const outDir = path.join(HERE, 'runs', sweepId);
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(path.join(outDir, 'effects.json'), JSON.stringify({ sweepId, families: [...bases.keys()], effects, errors, skipped, inconsistencies }, null, 2) + '\n');

// ---- effect-table.md ----
let md = `# Display-sweep effect table — ${sweepId}\n\nGenerated by derive.mjs. One row per state (one-lever mutation from base).\n`;
for (const family of bases.keys()) {
  const fam = effects.filter((e) => e.family === family);
  md += `\n## Family \`${family}\` (base \`${fam[0]?.base}\`) — ${fam.length} states\n\n`;
  md += '| state | mutation | effect paths | sections touched | leak paths | settled | stable |\n';
  md += '|---|---|---|---|---|---|---|\n';
  for (const e of fam) {
    const secs = Object.entries(sections(e.effectPaths)).map(([s, ps]) => `${s}(${ps.length})`).join(' ');
    md += `| ${e.stateId} | \`${JSON.stringify(e.mutation)}\` | ${e.effectCount}${e.effectCount >= MAX_PATHS ? '+' : ''} | ${secs || '—'} | ${e.leakCount} | ${e.settleMutated?.ok ? 'y' : 'TIMEOUT'} | ${e.stableMutated ? 'y' : 'UNSTABLE'} |\n`;
  }
}
fs.writeFileSync(path.join(outDir, 'effect-table.md'), md);

// ---- findings.md ----
const noOps = effects.filter((e) => e.effectCount === 0);
const leaks = effects.filter((e) => e.leakCount > 0);
const settleFails = effects.filter((e) => !e.settleMutated?.ok);
const unstable = effects.filter((e) => !e.stableMutated || !e.stableReverted);
const resetFails = effects.filter((e) => e.resetPreVerified === false || e.resetPostVerified === false);

let fm = `# Display-sweep findings — ${sweepId}\n`;
fm += `\nStates: ${effects.length} · skipped: ${skipped.length} · errors: ${errors.length} · no-ops: ${noOps.length} · leaks: ${leaks.length} · settle failures: ${settleFails.length} · unstable reads: ${unstable.length} · reset-verify failures: ${resetFails.length} · cross-family inconsistencies: ${inconsistencies.length} (${inconsistencies.filter((i) => i.crossBase).length} cross-base) · control violations: ${controlViolations.length}\n`;
const list = (title, arr, fmt) => {
  fm += `\n## ${title} (${arr.length})\n\n`;
  if (!arr.length) { fm += '_none_\n'; return; }
  for (const e of arr) fm += `- ${fmt(e)}\n`;
};
// Annotate no-ops whose mutation target was already at the base value —
// those are spec artifacts, not dead knobs / probe gaps.
const atBase = (e) => {
  const basePerf = bases.get(e.family)?.fp?.perf;
  if (!basePerf) return false;
  if (e.mutation.kind === 'perf') return JSON.stringify(basePerf[e.mutation.field]) === JSON.stringify(e.mutation.value);
  if (e.mutation.kind === 'junction') return basePerf.activeJunction === e.mutation.preset;
  return false;
};
list('No-op mutations (dead knobs OR probe gaps)', noOps, (e) => `\`${e.stateId}\` — ${JSON.stringify(e.mutation)} changed nothing observable from base \`${e.base}\`${atBase(e) ? ' *(target already at base value — spec artifact, not a finding)*' : ''}`);
list('State leaks (reverted fingerprint != family base)', leaks, (e) => `\`${e.stateId}\` — ${e.leakCount} path(s) leaked, e.g. ${e.leakPaths.slice(0, 3).map((p) => `\`${p.path}\``).join(', ')}`);
list('Settle failures', settleFails, (e) => `\`${e.stateId}\` — waitForRenderSettled timed out post-mutation`);
list('Unstable double-reads', unstable, (e) => `\`${e.stateId}\` — mutated:${e.stableMutated ? 'ok' : 'UNSTABLE'} reverted:${e.stableReverted ? 'ok' : 'UNSTABLE'}`);
list('Reset-verify failures (profile/touched not clean)', resetFails, (e) => `\`${e.stateId}\` — pre:${e.resetPreVerified} post:${e.resetPostVerified}`);
list('Driver errors', errors, (e) => `\`${e.stateId ?? '?'}\` — ${e.error}`);
list('Control violations (profile-to-self must be 0 effect / 0 leak)', controlViolations,
  (e) => `\`${e.family}\` — effect ${e.effectCount}, leak ${e.leakCount}`);
list('Skipped mutations (no derivable alternate — extend the alternates map to cover)',
  Object.entries(skipped.reduce((acc, s) => { (acc[s.stateId] ??= []).push(s.family); return acc; }, {})).map(([id, fams]) => ({ id, fams })),
  (s) => `\`${s.id}\` — ${s.fams.length} family(ies)`);

fm += `\n## Cross-family inconsistencies (${inconsistencies.length}; cross-base first)\n\n`;
fm += 'Same mutation, different effect signature across families. Cross-BASE rows (same fixture,\ndifferent base profile) are the primary "display dynamics inconsistent across personas" signal;\ncross-fixture-only rows may just reflect content differences.\n\n';
if (!inconsistencies.length) fm += '_none_\n';
for (const inc of inconsistencies) {
  fm += `- ${inc.crossBase ? '**[cross-base]**' : '[cross-fixture]'} \`${inc.stateId}\` — ${JSON.stringify(inc.mutation)}\n`;
  for (const pf of inc.perFamily) {
    fm += `  - ${pf.family}: ${pf.count} path(s)${pf.missing.length ? `; missing vs union: ${pf.missing.map((p) => `\`${p}\``).join(', ')}` : ''}\n`;
  }
}
fs.writeFileSync(path.join(outDir, 'findings.md'), fm);

console.log(`derived ${effects.length} states (${errors.length} errors) -> ${outDir}`);
console.log(`  no-ops: ${noOps.length} · leaks: ${leaks.length} · settle-fails: ${settleFails.length} · unstable: ${unstable.length}`);
