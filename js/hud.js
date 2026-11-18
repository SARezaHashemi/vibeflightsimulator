/**
 * Head-up display, painted straight onto the canvas in CSS pixels.
 */

import { FONT, HUD_TINT, ROTATE_SPEED } from './config.js';
import { AIRPORT } from './world.js';

const LABEL = '600 10px ' + FONT;
const VALUE = '600 30px ' + FONT;
const SMALL = '600 11px ' + FONT;
const BIG = '700 34px ' + FONT;
const BOLD = '700 13px ' + FONT;

const tint = (alpha) => HUD_TINT + alpha + ')';
const angleDelta = (a, b) => Math.atan2(Math.sin(a - b), Math.cos(a - b));

function label(ctx, text, x, y) {
  ctx.font = LABEL;
  ctx.fillStyle = tint(0.55);
  ctx.fillText(text, x, y);
}

/** Three gear lamps: green when locked down, amber in transit, dark when up. */
function drawGear(ctx, aircraft, W, H) {
  const y = H - 30;
  const lampW = 16;
  const lampH = 6;
  const gap = 4;
  const total = lampW * 3 + gap * 2;
  const x0 = W / 2 - total / 2;

  let colour;
  if (aircraft.gearDown && aircraft.gearPosition > 0.98) colour = '#5ef08a';
  else if (aircraft.gearPosition > 0.02) colour = '#f0c860';
  else colour = 'rgba(150,215,255,0.22)';

  ctx.textAlign = 'center';
  label(ctx, 'GEAR', W / 2, H - 46);
  ctx.fillStyle = colour;
  for (let i = 0; i < 3; i++) {
    ctx.fillRect(x0 + i * (lampW + gap), y, lampW, lampH);
  }
}

/** Distance and bearing to the airfield, with a turn arrow. */
function drawRunwayGuidance(ctx, aircraft, W) {
  const dx = AIRPORT.x - aircraft.position.x;
  const dz = AIRPORT.z - aircraft.position.z;
  const distance = Math.hypot(dx, dz);
  if (distance > 20000) return;

  const bearing = Math.atan2(dx, -dz);
  const offset = angleDelta(bearing, aircraft.headingRad);
  const arrow = offset > 0.26 ? '\u25b6' : offset < -0.26 ? '\u25c0' : '\u25b2';
  const km = (distance / 1000).toFixed(distance < 2000 ? 2 : 1);

  ctx.textAlign = 'center';
  ctx.font = SMALL;
  ctx.fillStyle = tint(0.7);
  ctx.fillText(`RWY ${km} KM  ${arrow}`, W / 2, 20);
}

export function drawHUD(ctx, aircraft, width, height) {
  const W = width;
  const H = height;

  ctx.textBaseline = 'top';
  ctx.textAlign = 'left';
  ctx.shadowColor = 'rgba(4,20,36,0.55)';
  ctx.shadowBlur = 7;

  /* ---- speed, top left ---- */
  const kmh = String(Math.round(aircraft.speed * 3.6));

  label(ctx, 'SPEED', 22, 20);
  ctx.font = VALUE;
  ctx.fillStyle = '#eaf7ff';
  ctx.fillText(kmh, 20, 34);

  const kmhWidth = ctx.measureText(kmh).width;
  ctx.font = LABEL;
  ctx.fillStyle = tint(0.6);
  ctx.fillText('KM/H', 20 + kmhWidth + 7, 50);
  const acc = aircraft.longitudinalAcceleration;
  ctx.fillText(
    `${Math.round(aircraft.speed)} M/S   ${acc >= 0 ? '+' : ''}${acc.toFixed(1)} M/S\u00b2`,
    20,
    72,
  );

  /* ---- aerodynamics: angle of attack and wing loading ---- */
  const aoa = aircraft.angleOfAttack * 180 / Math.PI;
  ctx.fillStyle = aircraft.isStalled ? '#ff8a7f' : tint(0.6);
  ctx.fillText(`AOA ${aoa.toFixed(1)}\u00b0   ${aircraft.gLoad.toFixed(1)} G`, 20, 88);

  /* ---- surface under the wheels ---- */
  if (aircraft.surface && (aircraft.isGrounded || aircraft.isCrashed)) {
    const kind = aircraft.surface.kind.toUpperCase();
    ctx.fillStyle = aircraft.surface.kind === 'rough' ? '#ffb26b' : tint(0.6);
    ctx.fillText(`SURFACE ${kind}`, 20, 104);
  }

  /* ---- altitude, top right ---- */
  ctx.textAlign = 'right';
  label(ctx, 'ALTITUDE', W - 22, 20);
  ctx.font = VALUE;
  ctx.fillStyle = '#eaf7ff';
  ctx.fillText(String(Math.round(aircraft.altitudeAgl)), W - 20, 34);

  ctx.font = LABEL;
  ctx.fillStyle = tint(0.6);
  ctx.fillText('M AGL', W - 24, 71);
  const vs = aircraft.verticalSpeed;
  ctx.fillText(`VS ${vs >= 0 ? '+' : ''}${Math.round(vs)} M/S`, W - 24, 87);

  /* ---- heading, bottom right ---- */
  label(ctx, 'HDG', W - 22, H - 46);
  ctx.font = '600 18px ' + FONT;
  ctx.fillStyle = '#eaf7ff';
  ctx.fillText(String(Math.round(aircraft.heading)).padStart(3, '0') + '\u00b0', W - 20, H - 32);

  /* ---- throttle, bottom left ---- */
  ctx.textAlign = 'left';
  label(ctx, 'THR', 22, H - 46);

  const barX = 22, barY = H - 30, barW = 120, barH = 8;
  ctx.fillStyle = tint(0.18);
  ctx.fillRect(barX, barY, barW, barH);
  // The faint bar is the lever; the bright one is what the engine has actually
  // spooled up to.
  ctx.fillStyle = tint(0.30);
  ctx.fillRect(barX, barY, barW * aircraft.throttle, barH);
  ctx.fillStyle = '#7fe0a8';
  ctx.fillRect(barX, barY, barW * aircraft.engine, barH);
  ctx.font = LABEL;
  ctx.fillStyle = tint(0.75);
  ctx.fillText(Math.round(aircraft.engine * 100) + '%', barX + barW + 10, H - 46);

  /* ---- gear, bottom centre ---- */
  drawGear(ctx, aircraft, W, H);

  /* ---- runway guidance, top centre ---- */
  drawRunwayGuidance(ctx, aircraft, W);

  /* ---- gun-sight ---- */
  const cx = W / 2;
  const cy = H / 2;
  ctx.strokeStyle = 'rgba(190,240,255,0.75)';
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(cx - 26, cy); ctx.lineTo(cx - 8, cy);
  ctx.moveTo(cx + 8, cy);  ctx.lineTo(cx + 26, cy);
  ctx.moveTo(cx, cy - 12); ctx.lineTo(cx, cy - 5);
  ctx.stroke();

  /* ---- stall warning ---- */
  if (aircraft.isStalled) {
    const blink = 0.45 + 0.55 * Math.abs(Math.sin(aircraft.elapsed * 7));
    ctx.textAlign = 'center';
    ctx.font = BOLD;
    ctx.fillStyle = `rgba(255,90,80,${blink.toFixed(2)})`;
    ctx.fillText('STALL', cx, cy + 60);
  }

  /* ---- touchdown banner ---- */
  if (aircraft.isGrounded) {
    const since = aircraft.elapsed - aircraft.landedAt;
    if (since < 4) {
      const alpha = since < 2.5 ? 1 : 1 - (since - 2.5) / 1.5;
      const landing = aircraft.lastLanding;
      const surface = landing ? landing.surface.toUpperCase() : 'RUNWAY';
      const firm = landing && landing.quality === 'firm';

      const title = firm
        ? 'FIRM LANDING'
        : surface === 'RUNWAY'
          ? 'LANDED'
          : `LANDED ON ${surface}`;

      ctx.textAlign = 'center';
      ctx.font = BOLD;
      ctx.fillStyle = firm
        ? `rgba(240,200,96,${alpha.toFixed(2)})`
        : `rgba(94,240,138,${alpha.toFixed(2)})`;
      ctx.fillText(title, cx, cy - 64);
    }
    ctx.textAlign = 'center';
    ctx.font = SMALL;
    ctx.fillStyle = tint(0.5);
    ctx.fillText('X to brake      S to rotate above ' + Math.round(ROTATE_SPEED * 3.6) + ' km/h', cx, cy + 68);
  }

  /* ---- controls hint, fades out after a few seconds ---- */
  if (aircraft.elapsed < 14 && !aircraft.isCrashed) {
    const alpha = aircraft.elapsed < 11 ? 1 : 1 - (aircraft.elapsed - 11) / 3;
    ctx.textAlign = 'center';
    ctx.font = SMALL;
    ctx.fillStyle = `rgba(210,240,255,${(alpha * 0.55).toFixed(2)})`;
    ctx.fillText('W / S  pitch  (S = nose up)      A / D  roll      Q / E  rudder', W / 2, H - 84);
    ctx.fillText('SHIFT  throttle up      X  throttle / brake      G  gear      C  view      R  restart', W / 2, H - 68);
  }

  /* ---- crash ---- */
  if (aircraft.isCrashed) {
    ctx.fillStyle = 'rgba(10,20,32,0.6)';
    ctx.fillRect(0, 0, W, H);

    ctx.textAlign = 'center';
    ctx.font = BIG;
    ctx.fillStyle = '#ff6b60';
    ctx.fillText('CRASHED', W / 2, H / 2 - 46);

    ctx.font = BOLD;
    ctx.fillStyle = '#ff9d94';
    ctx.fillText(aircraft.crashReason, W / 2, H / 2 - 6);

    ctx.font = '600 12px ' + FONT;
    ctx.fillStyle = 'rgba(220,240,255,0.75)';
    ctx.fillText('press  R  to fly again', W / 2, H / 2 + 26);

    const impact = aircraft.crashImpact;
    if (impact) {
      ctx.font = SMALL;
      ctx.fillStyle = 'rgba(220,240,255,0.45)';
      const sink = impact.sink > 0.05 ? `  \u00b7  ${impact.sink.toFixed(1)} m/s` : '';
      ctx.fillText(
        `impact ${Math.round(impact.speed * 3.6)} km/h${sink}`,
        W / 2,
        H / 2 + 50,
      );
      if (impact.surface && impact.surface !== 'runway') {
        ctx.fillText('on ' + impact.surface.toUpperCase(), W / 2, H / 2 + 66);
      }
    }
  }

  ctx.shadowBlur = 0;
  ctx.shadowColor = 'transparent';
}
