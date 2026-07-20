#!/usr/bin/env node
/**
 * drift-diff.mjs — compare a fresh sweep run against the dossier's
 * reference run and report display-behavior drift.
 *
 * Usage:
 *   node workloads/qualia/sweeps/drift-diff.mjs <candidateSweepId> [--reference <sweepId>]
 *
 * Reference defaults to REFERENCE-RUN.json in this directory (the run
 * Qualia/spec/DISPLAY-BEHAVIOR.md is provenance-stamped with). Exit code:
 * 0 = no drift, 1 = drift detected, 2 = usage/IO error — so the Hermes
 * drift-watch skill (MultiVerse/skills/qualia-display-sweep) can gate on it.
 *
 * Drift categories (per shared family × stateId):
 *   - activity flips  (ACTIVE ↔ INERT, errored ↔ clean)  — the loudest signal
 *   - signature changes (same activity, different normalized effect paths)
 *   - leak changes    (state-leak paths appeared / disappeared)
 * State-set differences (mutations present in only one run — e.g. a new
 * persona swept after a source change) are reported as INFO, not drift.
 *
 * Campaign: Qualia/docs/plans/2026-07-19-display-behavior-sweep.md.
 */

import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const candidateId = args.find((a) => !a.startsWith('--'));
if (!candidateId) { console.error('Usage: node drift-diff.mjs <candidateSweepId> [--reference <sweepId>]'); process.exit(2); }
const refIdx = args.indexOf('--reference');
let referenceId = refIdx >= 0 ? args[refIdx + 1] : null;
if (!referenceId) {
  const refPath = path.join(HERE, 'REFERENCE-RUN.json');
  if (!fs.existsSync(refPath)) { console.error('No --reference and no REFERENCE-RUN.json'); process.exit(2); }
  referenceId = JSON.parse(fs.readFileSync(refPath, 'utf8')).sweepId;
}

const load = (id) => {
  const p = path.join(HERE, 'runs', id, 'effects.json');
  if (!fs.existsSync(p)) { console.error(`effects.json not found for run '${id}' (${p})`); process.exit(2); }
  return JSON.parse(fs.readFileSync(p, 'utf8'));
};
const ref = load(referenceId);
const cand = load(candidateId);

// Same normalization as derive.mjs — content-independent effect signatures.
const normPath = (p) => p
  .replace(/perNode\.[^.]+\./, 'perNode.*.')
  .replace(/frame\.nodes\.[^.]+\./, 'frame.nodes.*.')
  .replace(/frame\.junctions\.[^.]+\./, 'frame.junctions.*.')
  .replace(/persona\.enabled\.[^.]+$/, 'persona.enabled.*');
const sig = (e) => [...new Set(e.effectPaths.map((p) => normPath(p.path)))].sort();
const leakSig = (e) => [...new Set(e.leakPaths.map((p) => normPath(p.path)))].sort();
const activity = (e) => (e.effectPaths.some((p) => !p.path.startsWith('persona.enabled')) ? 'ACTIVE' : 'INERT');

const key = (e) => `${e.family}|${e.stateId}`;
const index = (run) => {
  const m = new Map();
  for (const e of run.effects) m.set(key(e), e);
  const errs = new Set((run.errors ?? []).map((e) => `${e.family ?? '?'}|${e.stateId ?? '?'}`));
  return { m, errs };
};
const R = index(ref), C = index(cand);

const activityFlips = [], signatureChanges = [], leakChanges = [], errorFlips = [];
const onlyRef = [], onlyCand = [];

for (const [k, re] of R.m) {
  const ce = C.m.get(k);
  const candErrored = C.errs.has(k);
  if (!ce && !candErrored) { onlyRef.push(k); continue; }
  if (!ce && candErrored) { errorFlips.push({ key: k, was: 'clean', now: 'ERROR' }); continue; }
  const [ra, ca] = [activity(re), activity(ce)];
  if (ra !== ca) { activityFlips.push({ key: k, was: ra, now: ca, wasPaths: re.effectCount, nowPaths: ce.effectCount }); continue; }
  const [rs, cs] = [JSON.stringify(sig(re)), JSON.stringify(sig(ce))];
  if (rs !== cs) {
    const a = sig(re), b = sig(ce);
    signatureChanges.push({
      key: k,
      added: b.filter((p) => !a.includes(p)).slice(0, 8),
      removed: a.filter((p) => !b.includes(p)).slice(0, 8),
    });
  }
  const [rl, cl] = [JSON.stringify(leakSig(re)), JSON.stringify(leakSig(ce))];
  if (rl !== cl) {
    const a = leakSig(re), b = leakSig(ce);
    leakChanges.push({
      key: k,
      appeared: b.filter((p) => !a.includes(p)),
      disappeared: a.filter((p) => !b.includes(p)),
    });
  }
}
for (const k of C.m.keys()) if (!R.m.has(k)) onlyCand.push(k);
for (const k of R.errs) if (!C.errs.has(k) && C.m.has(k)) errorFlips.push({ key: k, was: 'ERROR', now: 'clean' });

const driftCount = activityFlips.length + signatureChanges.length + leakChanges.length + errorFlips.length;

let md = `# Drift report — ${candidateId} vs reference ${referenceId}\n\n`;
md += driftCount
  ? `**DRIFT DETECTED: ${driftCount} change(s).** The dossier (Qualia/spec/DISPLAY-BEHAVIOR.md) no longer matches measured behavior — fix the regression or deliberately update the dossier + REFERENCE-RUN.json.\n`
  : '**No drift.** Measured display behavior matches the dossier reference.\n';

const section = (title, arr, fmt) => {
  md += `\n## ${title} (${arr.length})\n\n`;
  if (!arr.length) { md += '_none_\n'; return; }
  for (const x of arr) md += `- ${fmt(x)}\n`;
};
section('Activity flips (loudest drift — wiring appeared/died)', activityFlips,
  (x) => `\`${x.key}\` — ${x.was} → **${x.now}** (${x.wasPaths} → ${x.nowPaths} paths)`);
section('Error flips', errorFlips, (x) => `\`${x.key}\` — ${x.was} → **${x.now}**`);
section('Leak changes (0055-class regressions)', leakChanges,
  (x) => `\`${x.key}\` — appeared: ${x.appeared.map((p) => `\`${p}\``).join(', ') || '—'}; disappeared: ${x.disappeared.map((p) => `\`${p}\``).join(', ') || '—'}`);
section('Effect-signature changes (same activity, different paths)', signatureChanges,
  (x) => `\`${x.key}\` — +[${x.added.join(', ')}] −[${x.removed.join(', ')}]`);
section('INFO: states only in reference (removed levers?)', onlyRef, (x) => `\`${x}\``);
section('INFO: states only in candidate (new levers — extend the dossier)', onlyCand, (x) => `\`${x}\``);

const outPath = path.join(HERE, 'runs', candidateId, `drift-vs-${referenceId}.md`);
fs.writeFileSync(outPath, md);
console.log(`${driftCount ? 'DRIFT: ' + driftCount + ' change(s)' : 'no drift'} — ${activityFlips.length} activity flips, ${errorFlips.length} error flips, ${leakChanges.length} leak changes, ${signatureChanges.length} signature changes; info: ${onlyRef.length} ref-only, ${onlyCand.length} cand-only`);
console.log(`report: ${outPath}`);
process.exit(driftCount ? 1 : 0);
