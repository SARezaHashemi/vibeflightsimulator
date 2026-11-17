/**
 * Entry point: wires the simulation, camera and renderers together and runs
 * the frame loop.
 */

import { Aircraft } from './aircraft.js';
import { Camera } from './camera.js';
import { TerrainRenderer } from './terrain.js';
import { drawSky, drawSun } from './sky.js';
import { drawHUD } from './hud.js';
import { runwayLayer } from './scenery.js';
import { treeLayers } from './trees.js';
import { cloudLayers } from './clouds.js';
import { drawAircraft } from './model.js';
import { COCKPIT_CULL } from './config.js';
import { Input } from './input.js';

const canvas = document.getElementById('viewport');
const ctx = canvas.getContext('2d', { alpha: false });

const aircraft = new Aircraft();
const camera = new Camera();
const terrain = new TerrainRenderer();

const view = { width: 1, height: 1, dpr: 1 };

function resize() {
  view.dpr = Math.min(window.devicePixelRatio || 1, 2);
  view.width = window.innerWidth;
  view.height = window.innerHeight;
  canvas.width = Math.round(view.width * view.dpr);
  canvas.height = Math.round(view.height * view.dpr);
}

window.addEventListener('resize', resize);
resize();

let cockpit = false;

const input = new Input(window, {
  onReset: () => aircraft.reset(),
  onGear: () => aircraft.toggleGear(),
  onView: () => { cockpit = !cockpit; },
});

function render(now) {
  ctx.setTransform(view.dpr, 0, 0, view.dpr, 0, 0);

  camera.update(aircraft, view.width, view.height, cockpit ? 'cockpit' : 'chase');

  drawSky(ctx, camera);
  drawSun(ctx, camera);
  terrain.draw(ctx, camera, [
    runwayLayer(camera),
    ...treeLayers(camera),
    ...cloudLayers(camera),
  ], now * 0.001);
  drawAircraft(ctx, camera, aircraft, cockpit ? COCKPIT_CULL : 0);
  drawHUD(ctx, aircraft, view.width, view.height);
}

let previous = performance.now();

function frame(now) {
  const dt = Math.min(0.05, (now - previous) / 1000);
  previous = now;

  aircraft.update(dt, input.axes);   // no-op once crashed

  render(now);
  requestAnimationFrame(frame);
}

requestAnimationFrame(frame);
