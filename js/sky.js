/**
 * Sky dome and sun.
 *
 * The horizon is a straight line on screen, so instead of shading the whole
 * sky we find two points on it, rotate the canvas to lie along that line and
 * paint one vertical gradient. Rolling and pitching the aircraft therefore
 * tilts and slides the sky exactly as it should.
 */

import { HAZE, SKY_STOPS, SUN_DIR, SUN_GLOW } from './config.js';

const HAZE_CSS = `rgb(${HAZE[0]},${HAZE[1]},${HAZE[2]})`;

export function drawSky(ctx, camera) {
  const { width: W, height: H } = camera;
  const fwd = camera.forward;
  const horizonLength = Math.hypot(fwd.x, fwd.z);

  if (horizonLength > 0.06) {
    const heading = Math.atan2(fwd.z, fwd.x);
    const a = camera.projectDirection({
      x: Math.cos(heading - 0.10), y: 0, z: Math.sin(heading - 0.10),
    });
    const b = camera.projectDirection({
      x: Math.cos(heading + 0.10), y: 0, z: Math.sin(heading + 0.10),
    });

    if (a && b) {
      const tilt = Math.atan2(b.y - a.y, b.x - a.x);

      ctx.save();
      ctx.translate(a.x, a.y);
      ctx.rotate(tilt);

      const gradient = ctx.createLinearGradient(0, -H * 1.15, 0, 0);
      for (const [stop, colour] of SKY_STOPS) gradient.addColorStop(stop, colour);

      ctx.fillStyle = gradient;
      ctx.fillRect(-W * 4, -H * 4, W * 8, H * 4);   // sky
      ctx.fillStyle = HAZE_CSS;
      ctx.fillRect(-W * 4, 0, W * 8, H * 4);        // ground haze
      ctx.restore();
      return;
    }
  }

  // Looking straight up or down: no meaningful horizon, flat sky.
  ctx.fillStyle = SKY_STOPS[1][1];
  ctx.fillRect(0, 0, W, H);
}

export function drawSun(ctx, camera) {
  const centre = camera.projectDirection(SUN_DIR);
  if (!centre) return;

  const radius = Math.max(camera.width, camera.height) * 0.16;
  const glow = ctx.createRadialGradient(centre.x, centre.y, 0, centre.x, centre.y, radius);
  glow.addColorStop(0.00, SUN_GLOW + '0.95)');
  glow.addColorStop(0.05, SUN_GLOW + '0.60)');
  glow.addColorStop(0.22, SUN_GLOW + '0.16)');
  glow.addColorStop(1.00, SUN_GLOW + '0)');

  ctx.fillStyle = glow;
  ctx.fillRect(centre.x - radius, centre.y - radius, radius * 2, radius * 2);
}
