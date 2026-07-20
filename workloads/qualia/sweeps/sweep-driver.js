/**
 * Display-sweep in-page driver (W1, 2026-07-19).
 *
 * Installed into the Qualia page by generated sweep tests as a
 * `setup.commands` step (the generator embeds this file's source via
 * JSON.stringify — keep it dependency-free, plain script, no modules).
 * Campaign: Qualia/docs/plans/2026-07-19-display-behavior-sweep.md ·
 * driver prompt MultiVerse/prompts/qualia-display-sweep-2026-07-19.md.
 *
 * Protocol per state (the nine verified ground rules live in the
 * campaign prompt — the load-bearing ones here):
 *   reset  = __canaryClearTouchedPerfFields + __canaryApplyProfile(base)
 *            + theme/selection/camera restore (re-applying the SAME
 *            profile alone does NOT clear touches; persona toggles use
 *            preserveProfile so the profile layer survives)
 *   mutate = ONE lever (persona / perf / junction / theme / profile / planar)
 *   settle = __canaryWaitForRenderSettled (err-envelope, never throws)
 *   fp     = structural fingerprint, double-read one rAF apart
 *   revert = full reset, fingerprint again (state-leak detector)
 *
 * Observations: one JSON file per state via the dev server's
 * POST /api/debug/write (Qualia/debug-logs/<sweepId>/...). Console gets
 * a compact CANARY_SWEEP| line per state as a telemetry backup channel.
 */
(() => {
  const w = window;
  // Envelope unwrap — TOLERANT of pre-envelope hooks: __canaryGetPersonaConfig
  // (and a few other early hooks) return the raw value, not { ok, value }.
  // Raw objects pass through; only explicit { ok:false } becomes __err.
  const un = (env) => {
    if (env && env.ok === true) return env.value;
    if (env && env.ok === false) return { __err: env.reason || 'err' };
    return env === undefined ? { __err: 'undefined-return' } : env;
  };
  const raf = () => new Promise((r) => requestAnimationFrame(() => r()));

  const S = { ctx: null, camera: null, baseTheme: null, fpBase: null, seq: 0 };

  async function settle(timeoutMs) {
    const t0 = performance.now();
    const res = await w.__canaryWaitForRenderSettled(timeoutMs || 5000);
    return { ok: !!(res && res.ok), ms: Math.round(performance.now() - t0) };
  }

  async function writeObs(filename, payload) {
    const res = await fetch('/api/debug/write', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        session: S.ctx.sweepId,
        filename,
        content: JSON.stringify(payload),
      }),
    });
    if (!res.ok) throw new Error('debug-write failed: HTTP ' + res.status + ' for ' + filename);
  }

  /**
   * Persona-verification W1 — scene-graph section. Counts objects by
   * three.js type + enabled pipeline passes. Catches personas that
   * add/remove scene objects (junction-marker Points, force-field
   * InstancedMesh, heat-map plane, stage lights) and pass-gated post
   * effects — the atlas's structural blind spot. Reaches renderer
   * internals deliberately (driver = instrumentation layer).
   */
  function sceneGraphSnapshot() {
    try {
      const r = w.__qualiaRenderer;
      const sm = r && r.getSceneManager ? r.getSceneManager() : null;
      if (!sm || !sm.scene) return { __err: 'no-scene' };
      const byType = {};
      let total = 0;
      sm.scene.traverse((o) => { total++; byType[o.type] = (byType[o.type] || 0) + 1; });
      let passes = null;
      const ph = sm._pipelineHost;
      if (ph && typeof ph.listEnabled === 'function') {
        passes = ph.listEnabled().map((p) => p.id || p.name || 'pass').sort();
      }
      return { total, byType, enabledPasses: passes };
    } catch (e) {
      return { __err: String(e && e.message ? e.message : e) };
    }
  }

  /**
   * Persona-verification W1 — DOM section. Targeted, stable probes:
   * the canvas-host classList (stub marker classes `qualia-<id>-active`
   * + label-bloom's container class land there) and known persona-owned
   * panels. Verified selectors (FpsHud.tsx, QverseNavigator.tsx,
   * ContextJewelHud.tsx, stubs.ts, labelBloom.ts).
   */
  function domSnapshot() {
    try {
      const canvas = document.querySelector('canvas');
      const hostClasses = canvas && canvas.parentElement
        ? Array.prototype.slice.call(canvas.parentElement.classList).sort() : [];
      const n = (q) => document.querySelectorAll(q).length;
      return {
        hostClasses,
        fpsHud: n('.qualia-fps-hud'),
        qverseNavigator: n('.qualia-qverse-navigator'),
        jewelHud: n('.qualia-context-jewel-hud'),
        simBadge: n('[class*="sim-status"], [class*="eager-extraction-badge"]'),
        toolbarButtons: n('[class*="persona-toolbar"] button, [data-persona-button]'),
        // styleCount deliberately EXCLUDED — injected stylesheets never
        // retract (label-bloom keeps its <style> after dispose), making
        // the count monotonic noise (w1-personas-r1: false positives on
        // no-reader personas + 26 phantom leaks).
      };
    } catch (e) {
      return { __err: String(e && e.message ? e.message : e) };
    }
  }

  function fingerprint() {
    const nodes = un(w.__canaryListNodes());
    const perNode = {};
    if (Array.isArray(nodes)) {
      for (const n of nodes) {
        perNode[n.id] = {
          rendered: un(w.__canaryGetRenderedNodePosition(n.id)),
          fadeAlpha: un(w.__canaryGetNodeFadeAlpha(n.id)),
          mode: un(w.__canaryGetResolvedNodeDisplayMode(n.id)),
        };
      }
    }
    const stats = un(w.__canaryGetDebugStats());
    return {
      persona: un(w.__canaryGetPersonaConfig()),
      touched: un(w.__canaryGetTouchedPerfFields()),
      perf: un(w.__canaryGetPerfSettings()),
      theme: un(w.__canaryGetThemeState()),
      grid: un(w.__canaryGetGridVisible()),
      edgeShape: un(w.__canaryGetEdgeShape()),
      edgeRouting: un(w.__canaryGetEdgeRouting()),
      viewer: un(w.__canaryGetViewerSettings()),
      planar: un(w.__canaryGetPlanarSettings()),
      socket: un(w.__canaryGetSocketState()),
      halo: un(w.__canaryGetHaloState()),
      nubs: un(w.__canaryGetNubVariantCounts()),
      frame: un(w.__canaryGetFrameGeometry({ includeJunctions: true })),
      // W1 fingerprint v2 — closes the scene-graph + DOM blind spots.
      // Reference runs older than fingerprint v2 will show these as
      // all-new paths; re-baseline deliberately after adopting.
      sceneGraph: sceneGraphSnapshot(),
      dom: domSnapshot(),
      perNode,
      // DebugStats whitelist — memoryMB / programs / draw counters are
      // monotonic or frame-nondeterministic (verified ground rule 8).
      stats: stats && !stats.__err
        ? { nodeCount: stats.nodeCount, edgeCount: stats.edgeCount, groupCount: stats.groupCount, activeContextId: stats.activeContextId }
        : stats,
    };
  }

  async function doubleRead() {
    const a = fingerprint();
    await raf(); await raf();
    const b = fingerprint();
    return { fp: b, stable: JSON.stringify(a) === JSON.stringify(b) };
  }

  function applyDisables() {
    // Family-level persona disables (e.g. compute.layout — layout bases
    // never settle). Re-applied after EVERY applyProfile since a profile
    // apply re-enables its declared personas. preserveProfile keeps the
    // named profile intact.
    const ids = (S.ctx && S.ctx.disablePersonas) || [];
    for (const id of ids) w.__canarySetPersonaEnabled(id, false, { preserveProfile: true });
  }

  async function resetToBase() {
    w.__canaryClearTouchedPerfFields();
    w.__canaryApplyProfile(S.ctx.base);
    applyDisables();
    if (S.baseTheme) w.__canarySetTheme(S.baseTheme);
    w.__canaryClearSelection();
    if (S.camera) {
      w.__canarySetCameraState({ position: S.camera.position, target: S.camera.target }, 0);
    }
    const st = await settle();
    const cfg = un(w.__canaryGetPersonaConfig());
    const touched = un(w.__canaryGetTouchedPerfFields());
    return {
      settle: st,
      verified: cfg && cfg.profile === S.ctx.base && Array.isArray(touched) && touched.length === 0,
      profile: cfg ? cfg.profile : null,
      touchedCount: Array.isArray(touched) ? touched.length : -1,
    };
  }

  /**
   * Derive an alternate value for a perfAuto mutation from the BASE
   * fingerprint. Returns { value } or { skip: reason }. Enums use the
   * ctx.alternates map (observed values only); unknown strings skip.
   */
  function deriveAlternate(field) {
    const cur = S.fpBase && S.fpBase.perf ? S.fpBase.perf[field] : undefined;
    const alts = (S.ctx.alternates && S.ctx.alternates[field]) || null;
    if (alts) {
      const alt = alts.find((v) => JSON.stringify(v) !== JSON.stringify(cur));
      return alt !== undefined ? { value: alt } : { skip: 'no-differing-alternate' };
    }
    if (typeof cur === 'boolean') return { value: !cur };
    if (typeof cur === 'number') return { value: cur === 0 ? 0.5 : Math.round(cur * 150) / 100 };
    if ((cur === null || typeof cur === 'string') && /Color$/.test(field)) return { value: '#ff0066' };
    if (cur === undefined) return { skip: 'field-not-in-base-perf' };
    return { skip: 'no-alternate-for-' + typeof cur };
  }

  function applyMutation(m) {
    switch (m.kind) {
      case 'pair': {
        // W3 interaction mining: apply two levers in sequence. The
        // deriver compares sig(ab) against sig(a) UNION sig(b) from the
        // sibling single states the generator emits alongside.
        const ra = applyMutation(m.a);
        if (ra && ra.__skip) return ra;
        const rb = applyMutation(m.b);
        if (rb && rb.__skip) return rb;
        return { a: ra, b: rb };
      }
      case 'perfAuto': {
        const d = deriveAlternate(m.field);
        if (d.skip) return { __skip: d.skip };
        const partial = {}; partial[m.field] = d.value;
        const res = un(w.__canarySetPerfSettings(partial, { markTouched: false }));
        return { applied: res, derivedValue: d.value, baseValue: S.fpBase.perf[m.field] };
      }
      case 'persona': {
        const cfg = un(w.__canaryGetPersonaConfig());
        const cur = !!(cfg && cfg.enabled && cfg.enabled[m.id]);
        const target = m.enabled !== undefined ? m.enabled : !cur;
        return un(w.__canarySetPersonaEnabled(m.id, target, { preserveProfile: true }));
      }
      case 'perf': {
        const partial = {}; partial[m.field] = m.value;
        return un(w.__canarySetPerfSettings(partial, { markTouched: false }));
      }
      case 'junction':
        return un(w.__canarySetPerfSettings({ activeJunction: m.preset }, { markTouched: false }));
      case 'theme':
        return un(w.__canarySetTheme(m.value));
      case 'profile': {
        const res = un(w.__canaryApplyProfile(m.to));
        // Keep family-level disables in force through profile mutations —
        // otherwise a profile that re-enables a disabled persona (e.g.
        // compute.layout under workshop) reads as a false control
        // violation against the family base (w4-fix-verify-r1 lesson).
        applyDisables();
        return res;
      }
      case 'planar':
        return un(w.__canarySetPlanarSettings(m.value));
      default:
        throw new Error('unknown mutation kind: ' + m.kind);
    }
  }

  async function loadFixture(fixture) {
    if (fixture.kind === 'ddv') {
      const r = w.__canaryLoadDDV({ select: false });
      if (!r || !r.ok) throw new Error('LoadDDV failed: ' + JSON.stringify(r));
    } else if (fixture.kind === 'demo') {
      const r = await w.__canaryLoadDemo(fixture.slug);
      if (!r || r.ok !== true) throw new Error('LoadDemo(' + fixture.slug + ') failed: ' + JSON.stringify(r));
    } else {
      throw new Error('unknown fixture kind: ' + fixture.kind);
    }
    await new Promise((r) => setTimeout(r, 300));
  }

  w.__sweep = {
    /** Family init: fixture + base profile + camera pin + base fingerprint. */
    async init(ctx) {
      S.ctx = ctx;
      await loadFixture(ctx.fixture);
      if (ctx.enterContext) {
        // W2 content-arming — enter the named context and wait out the
        // CS-A.5 transition tween before pinning anything. Context is
        // out-of-resolver state: resets do NOT leave it, so the whole
        // family runs inside this context.
        const sw = un(w.__canarySwitchContext(ctx.enterContext));
        if (sw && sw.__err) throw new Error('enterContext failed: ' + sw.__err);
        await settle(8000);
        await new Promise((r) => setTimeout(r, 900));
      }
      w.__canaryClearTouchedPerfFields();
      w.__canaryApplyProfile(ctx.base);
      applyDisables();
      w.__canaryClearSelection();
      const st1 = await settle(8000);
      // Pin the camera AFTER the first settle — the one-shot auto-fit
      // fires ~100ms after first layout and would knock off an early pin.
      await new Promise((r) => setTimeout(r, 400));
      const cam = un(w.__canaryGetCameraState());
      if (cam && !cam.__err) {
        S.camera = { position: cam.position, target: cam.target };
        w.__canarySetCameraState(S.camera, 0);
      }
      const theme = un(w.__canaryGetThemeState());
      S.baseTheme = theme && theme.theme ? theme.theme : (theme && theme.current ? theme.current : 'dark');
      await settle();
      const base = await doubleRead();
      S.fpBase = base.fp;
      await writeObs('base-' + ctx.family + '.json', {
        kind: 'base', sweepId: ctx.sweepId, family: ctx.family, base: ctx.base,
        fixture: ctx.fixture, camera: S.camera, theme: S.baseTheme,
        settleInit: st1, stable: base.stable, fp: base.fp, ts: Date.now(),
      });
      console.log('CANARY_SWEEP|init|' + ctx.family + '|stable=' + base.stable);
      return 'sweep init ok: family=' + ctx.family + ' stable=' + base.stable;
    },

    /** Run one chunk of states. Each state: reset -> mutate -> fp -> reset -> fp. */
    async runChunk(states, chunkIdx) {
      if (!S.ctx) throw new Error('__sweep.init not run');
      let okCount = 0, errCount = 0;
      for (const st of states) {
        S.seq++;
        // Family goes in the filename — every family in a sweep shares one
        // observations dir and its own seq counter (a bare obs-<seq> name
        // collided across families and silently overwrote, w2-atlas-r1).
        const fileBase = 'obs-' + S.ctx.family + '-' + String(S.seq).padStart(3, '0') + '-' + st.id;
        try {
          const resetPre = await resetToBase();
          const applied = applyMutation(st.mutation);
          if (applied && applied.__skip) {
            await writeObs(fileBase + '.json', {
              kind: 'skipped', sweepId: S.ctx.sweepId, family: S.ctx.family,
              base: S.ctx.base, seq: S.seq, stateId: st.id, mutation: st.mutation,
              reason: applied.__skip, ts: Date.now(),
            });
            okCount++;
            console.log('CANARY_SWEEP|state|' + st.id + '|skipped:' + applied.__skip);
            continue;
          }
          const stMut = await settle();
          const mut = await doubleRead();
          const resetPost = await resetToBase();
          const rev = await doubleRead();
          await writeObs(fileBase + '.json', {
            kind: 'state', sweepId: S.ctx.sweepId, family: S.ctx.family,
            base: S.ctx.base, fixture: S.ctx.fixture, chunk: chunkIdx,
            seq: S.seq, stateId: st.id, mutation: st.mutation,
            applied, resetPre, resetPost,
            settleMutated: stMut,
            stableMutated: mut.stable, stableReverted: rev.stable,
            fpMutated: mut.fp, fpReverted: rev.fp, ts: Date.now(),
          });
          okCount++;
          console.log('CANARY_SWEEP|state|' + st.id + '|ok');
        } catch (e) {
          errCount++;
          try {
            await writeObs(fileBase + '-error.json', {
              kind: 'error', sweepId: S.ctx.sweepId, family: S.ctx.family,
              seq: S.seq, stateId: st.id, mutation: st.mutation,
              error: String(e && e.message ? e.message : e), ts: Date.now(),
            });
          } catch (_) { /* obs channel down — the console line is the record */ }
          console.log('CANARY_SWEEP|state|' + st.id + '|ERROR|' + e);
        }
      }
      return 'chunk ' + chunkIdx + ': ' + okCount + ' ok, ' + errCount + ' errors';
    },

    /**
     * W3 planar deep walk. Switches into every context of the loaded
     * fixture (waiting out the ~600ms CS-A.5 transition tween), records
     * a planar invariant snapshot, then probes captureLevel/uncapture.
     * Raw frame geometry + rendered positions ride in each record so
     * the deriver computes ADR-0038 drift, junction attach ratios, and
     * fade-table conformance offline.
     */
    async runPlanarWalk() {
      if (!S.ctx) throw new Error('__sweep.init not run');
      const list = un(w.__canaryListContexts());
      const ctxs = Array.isArray(list) ? list : [];
      let n = 0, errs = 0;

      const planarRecord = (c, phase) => {
        const nodes = un(w.__canaryListNodes());
        const perNode = {};
        if (Array.isArray(nodes)) {
          for (const nd of nodes) {
            perNode[nd.id] = {
              rendered: un(w.__canaryGetRenderedNodePosition(nd.id)),
              fadeAlpha: un(w.__canaryGetNodeFadeAlpha(nd.id)),
            };
          }
        }
        return {
          kind: 'planar', sweepId: S.ctx.sweepId, family: S.ctx.family,
          base: S.ctx.base, fixture: S.ctx.fixture, contextId: c.id,
          contextLabel: c.label, parentContextId: c.parentContextId,
          phase,
          deviation: un(w.__canaryGetPlaneDeviation()),
          planar: un(w.__canaryGetPlanarSettings()),
          lock: un(w.__canaryGetPerspectiveLock()),
          camera: un(w.__canaryGetCameraState()),
          frame: un(w.__canaryGetFrameGeometry({ includeJunctions: true })),
          perNode,
          activeScope: un(w.__canaryGetActiveScope()),
          ts: Date.now(),
        };
      };

      for (const c of ctxs) {
        n++;
        const safe = c.id.replace(/[^a-zA-Z0-9_-]/g, '_');
        try {
          const sw = un(w.__canarySwitchContext(c.id));
          if (sw && sw.__err) throw new Error('switch failed: ' + sw.__err);
          await settle(8000);
          await new Promise((r) => setTimeout(r, 900));
          await settle();
          await writeObs('planar-' + S.ctx.family + '-' + safe + '.json', planarRecord(c, 'entered'));
          const cap = un(w.__canaryCaptureLevel());
          if (cap && cap.levelId) {
            await settle();
            const rec2 = planarRecord(c, 'captured');
            rec2.capturedLevelId = cap.levelId;
            await writeObs('planar-' + S.ctx.family + '-' + safe + '-captured.json', rec2);
            un(w.__canaryUncaptureLevel(cap.levelId));
            await settle();
          }
          console.log('CANARY_SWEEP|planar|' + c.id + '|ok');
        } catch (e) {
          errs++;
          try {
            await writeObs('planar-' + S.ctx.family + '-' + safe + '-error.json', {
              kind: 'error', sweepId: S.ctx.sweepId, family: S.ctx.family,
              stateId: 'planar-' + c.id, mutation: { kind: 'planarWalk', contextId: c.id },
              error: String(e && e.message ? e.message : e), ts: Date.now(),
            });
          } catch (_) { /* console line is the record */ }
          console.log('CANARY_SWEEP|planar|' + c.id + '|ERROR|' + e);
        }
      }
      un(w.__canarySwitchContext(null));
      await settle();
      return 'planar walk: ' + n + ' contexts, ' + errs + ' errors';
    },
  };
  return '__sweep driver installed';
})()
