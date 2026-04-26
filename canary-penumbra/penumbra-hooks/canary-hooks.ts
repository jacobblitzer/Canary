// ─── Canary Integration Hooks ─────────────────────────────────────────────────
// Add this block to test/main.ts AFTER the renderer, camera, and scenes are initialized.
// These expose a minimal API surface on `window` so the Canary bridge agent can
// control the test harness via CDP Runtime.evaluate calls.
//
// The bridge agent calls these like:
//   await cdp.EvaluateAsync("window.__canarySetScene(0)")
//   await cdp.EvaluateAsync("window.__canarySetCamera(45, 30, 8)")
//   const info = await cdp.EvaluateAsync("window.__canaryGetRendererInfo()")
//
// This is the Penumbra equivalent of what the Rhino agent does natively via
// RhinoCommon API — but exposed as JS functions for CDP access.
// ──────────────────────────────────────────────────────────────────────────────

// --- Type declaration (add near the top of main.ts, after imports) ---

declare global {
    interface Window {
        /** When true, the render loop does NOT resize the canvas to match the window. */
        __canaryLockSize?: boolean;
        /** Load a scene by index. Returns a promise that resolves when the scene is ready. */
        __canarySetScene?: (index: number) => Promise<void>;
        /** Set the orbit camera to exact spherical coordinates (degrees). */
        __canarySetCamera?: (azimuthDeg: number, elevationDeg: number, distance: number) => void;
        /** Get the current camera position as spherical coordinates. */
        __canaryGetCamera?: () => { azimuth: number; elevation: number; distance: number };
        /** Get renderer state for heartbeat / readiness checks. */
        __canaryGetRendererInfo?: () => Record<string, unknown>;
        /** Pause the torus orbit animation (scene 2) for deterministic captures. */
        __canaryPauseAnimation?: boolean;
        /** Toggle a debug overlay by key. Returns the new enabled state. */
        __canaryToggleOverlay?: (key: string) => boolean;
        /** Get the current state of all debug overlays. */
        __canaryGetOverlayState?: () => Record<string, boolean>;
        /** Switch evaluation mode for all fields ('atlas' | 'analytical'). Returns the mode that was set. */
        __canarySetEvalMode?: (mode: string) => Promise<string>;
        /** Show or hide an individual field by index. Returns the new visibility state. */
        __canarySetFieldVisibility?: (fieldIndex: number, visible: boolean) => boolean;
        /** Set the canvas clear/background color. Returns the color that was set. */
        __canarySetBackground?: (color: string) => string;
        /** Get the list of available scenes with names and indices. */
        __canaryGetSceneList?: () => Array<{ index: number; name: string }>;
        /** Force a synchronous frame render. Returns when the frame is complete. */
        __canaryForceRedraw?: () => Promise<void>;
        /** Diagnostic: return brick overlay data pipeline state for debugging. */
        __canaryDiagnoseBricks?: () => Record<string, unknown>;
    }
}

// --- API implementation (add after renderer + camera + scenes are initialized) ---

window.__canaryLockSize = false;

window.__canarySetScene = async (index: number) => {
    if (index < 0 || index >= scenes.length) {
        throw new Error(`Scene index ${index} out of range (0-${scenes.length - 1})`);
    }

    // Pause animation for deterministic captures
    window.__canaryPauseAnimation = true;

    await loadScene(index);

    // Wait for atlas build if any fields use atlas evaluation
    const fields = renderer.getFields();
    const hasAtlas = fields.some(f => f.evalMode === 'atlas');
    if (hasAtlas) {
        // Poll until atlas is complete (up to 10 seconds)
        const deadline = performance.now() + 10000;
        while (performance.now() < deadline) {
            if (renderer.isAtlasBuildComplete()) break;
            await new Promise(r => setTimeout(r, 200));
        }
    }

    // Let a few frames render so the output stabilizes
    await new Promise(r => setTimeout(r, 300));
};

window.__canarySetCamera = (azimuthDeg: number, elevationDeg: number, distance: number) => {
    // Convert degrees to radians for the orbit camera
    const az = azimuthDeg * Math.PI / 180;
    const el = elevationDeg * Math.PI / 180;

    // Set camera position on the orbit sphere
    // The orbit camera stores: position, target, distance
    // position = target + spherical_to_cartesian(azimuth, elevation, distance)
    const target = camera.target || [0, 0, 0];
    const x = target[0] + distance * Math.cos(el) * Math.sin(az);
    const y = target[1] + distance * Math.sin(el);
    const z = target[2] + distance * Math.cos(el) * Math.cos(az);

    camera.position = [x, y, z];
    camera.distance = distance;

    // If the camera has a setSpherical method, prefer that
    if (typeof (camera as any).setSpherical === 'function') {
        (camera as any).setSpherical(azimuthDeg, elevationDeg, distance);
    }
};

window.__canaryGetCamera = () => {
    // If the camera has a getSpherical method, use it
    if (typeof (camera as any).getSpherical === 'function') {
        return (camera as any).getSpherical();
    }

    // Otherwise compute from position
    const target = camera.target || [0, 0, 0];
    const dx = camera.position[0] - target[0];
    const dy = camera.position[1] - target[1];
    const dz = camera.position[2] - target[2];
    const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
    const el = Math.asin(dy / dist) * 180 / Math.PI;
    const az = Math.atan2(dx, dz) * 180 / Math.PI;
    return { azimuth: az, elevation: el, distance: dist };
};

window.__canaryGetRendererInfo = () => {
    const fields = renderer.getFields();
    const hasAtlasFields = fields.some(f => f.evalMode === 'atlas');
    return {
        backend: renderer.getBackendName?.() ?? 'unknown',
        fieldCount: fields.length,
        hasAtlasFields,
        atlasBuildComplete: renderer.isAtlasBuildComplete(),
        sceneName: scenes[currentSceneIdx]?.name ?? 'unknown',
        sceneIndex: currentSceneIdx,
        fps: currentFps ?? 0,
    };
};

window.__canaryToggleOverlay = (key: string): boolean => {
    const valid = ['cascades', 'bricks', 'fieldAABBs', 'atomAABBs', 'foreignObjects', 'pointCloud'];
    if (!valid.includes(key)) return false;
    renderer.debugOverlay.toggle(key as keyof OverlayConfig);
    return renderer.debugOverlay.getConfig()[key as keyof OverlayConfig];
};

window.__canaryGetOverlayState = (): Record<string, boolean> => {
    const config = renderer.debugOverlay.getConfig();
    return {
        cascades: !!config.cascades,
        bricks: !!config.bricks,
        fieldAABBs: !!config.fieldAABBs,
        atomAABBs: !!config.atomAABBs,
        foreignObjects: !!config.foreignObjects,
        pointCloud: !!config.pointCloud,
    };
};

window.__canarySetEvalMode = async (mode: string): Promise<string> => {
    const valid = ['atlas', 'analytical'];
    if (!valid.includes(mode)) {
        throw new Error(`Invalid eval mode '${mode}'. Valid: ${valid.join(', ')}`);
    }

    const fields = renderer.getFields();
    for (const field of fields) {
        field.evalMode = mode as 'atlas' | 'analytical';
    }

    // If switching to atlas, wait for the atlas build to complete
    if (mode === 'atlas') {
        renderer.rebuildAtlas();
        const deadline = performance.now() + 10000;
        while (performance.now() < deadline) {
            if (renderer.isAtlasBuildComplete()) break;
            await new Promise(r => setTimeout(r, 200));
        }
    }

    // Let a few frames render so the output stabilizes
    await new Promise(r => setTimeout(r, 300));
    return mode;
};

window.__canarySetFieldVisibility = (fieldIndex: number, visible: boolean): boolean => {
    const fields = renderer.getFields();
    if (fieldIndex < 0 || fieldIndex >= fields.length) {
        throw new Error(`Field index ${fieldIndex} out of range (0-${fields.length - 1})`);
    }
    fields[fieldIndex].visible = visible;
    return fields[fieldIndex].visible;
};

window.__canarySetBackground = (color: string): string => {
    renderer.setClearColor(color);
    return color;
};

window.__canaryGetSceneList = (): Array<{ index: number; name: string }> => {
    return scenes.map((s: any, i: number) => ({ index: i, name: s.name ?? `Scene ${i}` }));
};

window.__canaryForceRedraw = async (): Promise<void> => {
    renderer.renderFrame();
    // Wait one rAF to ensure the GPU has flushed
    await new Promise(r => requestAnimationFrame(r));
};

window.__canaryDiagnoseBricks = (): Record<string, unknown> => {
    const overlayData = renderer.debugOverlay.getSceneData();
    const overlayConfig = renderer.debugOverlay.getConfig();
    const bricks = overlayData.bricks ?? [];
    const cascades = overlayData.cascades ?? [];
    const info = renderer.getRendererInfo?.() ?? {};
    return {
        atlasBuildComplete: (renderer as any).atlasBuildComplete ?? false,
        overlayConfig: { ...overlayConfig },
        brickCount: bricks.length,
        cascadeCount: cascades.length,
        cascades: cascades.map((c: any) => ({
            center: c.center,
            extent: c.extent,
            coarseRes: c.coarseRes,
        })),
        first5Bricks: bricks.slice(0, 5).map((b: any) => ({
            min: b.min,
            max: b.max,
            cascadeIndex: b.cascadeIndex,
            halfSize: b.min && b.max ? [
                ((b.max[0] - b.min[0]) / 2).toFixed(4),
                ((b.max[1] - b.min[1]) / 2).toFixed(4),
                ((b.max[2] - b.min[2]) / 2).toFixed(4),
            ] : 'N/A',
            hasNaN: b.min ? (isNaN(b.min[0]) || isNaN(b.min[1]) || isNaN(b.min[2]) ||
                             isNaN(b.max[0]) || isNaN(b.max[1]) || isNaN(b.max[2])) : true,
        })),
        rendererInfo: info,
    };
};


// --- Canvas size lock (modify the existing resize logic in the render loop) ---
// Find the place in frame() where canvas dimensions are set and wrap it:

// BEFORE (existing code in frame()):
//   canvas.width = canvas.clientWidth * devicePixelRatio;
//   canvas.height = canvas.clientHeight * devicePixelRatio;

// AFTER (with Canary lock):
//   if (!window.__canaryLockSize) {
//       canvas.width = canvas.clientWidth * devicePixelRatio;
//       canvas.height = canvas.clientHeight * devicePixelRatio;
//   }


// --- Animation pause (modify the torus orbit in frame()) ---
// Find the torus animation block in frame() and guard it:

// BEFORE:
//   if (currentSceneIdx === 2) {
//       const t = performance.now() * 0.001;
//       ...

// AFTER:
//   if (currentSceneIdx === 2 && !window.__canaryPauseAnimation) {
//       const t = performance.now() * 0.001;
//       ...


// --- Orbit Camera: add setSpherical / getSpherical methods ---
// Add these to the OrbitCamera class (or equivalent):

/*
setSpherical(azimuthDeg: number, elevationDeg: number, distance: number): void {
    const az = azimuthDeg * Math.PI / 180;
    const el = elevationDeg * Math.PI / 180;
    this.position[0] = this.target[0] + distance * Math.cos(el) * Math.sin(az);
    this.position[1] = this.target[1] + distance * Math.sin(el);
    this.position[2] = this.target[2] + distance * Math.cos(el) * Math.cos(az);
    this.distance = distance;
    // Mark camera dirty so the next getWorldMatrix() recomputes
    this._dirty = true;
}

getSpherical(): { azimuth: number; elevation: number; distance: number } {
    const dx = this.position[0] - this.target[0];
    const dy = this.position[1] - this.target[1];
    const dz = this.position[2] - this.target[2];
    const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
    const el = Math.asin(dy / dist) * 180 / Math.PI;
    const az = Math.atan2(dx, dz) * 180 / Math.PI;
    return { azimuth: az, elevation: el, distance: dist };
}
*/
