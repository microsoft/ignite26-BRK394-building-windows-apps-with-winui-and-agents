// Canvas 2D charts — sparkline, availability, vibration. Redraw on resize + theme change.
(function () {
  'use strict';
  const { fitCanvas, css, theme } = window.UI;

  function hexA(color, a) {
    // color is #RRGGBB or #RRGGBBAA
    const c = color.trim();
    const r = parseInt(c.substr(1, 2), 16), g = parseInt(c.substr(3, 2), 16), b = parseInt(c.substr(5, 2), 16);
    return `rgba(${r},${g},${b},${a})`;
  }

  function bind(canvas, draw) {
    let raf = 0;
    const schedule = () => { if (!raf) raf = requestAnimationFrame(() => { raf = 0; if (canvas.isConnected) draw(); }); };
    const ro = new ResizeObserver(schedule);
    ro.observe(canvas);
    const off = theme.onChanged(schedule);
    schedule();
    return { redraw: schedule, destroy() { ro.disconnect(); off(); cancelAnimationFrame(raf); } };
  }

  function sparkline(canvas, source, colorVar) {
    return bind(canvas, () => {
      const values = typeof source === 'function' ? source() : source;
      const { ctx, w, h } = fitCanvas(canvas);
      if (w < 4 || h < 4 || !values || values.length < 2) return;
      const color = css(colorVar);
      const inset = 4;
      const min = Math.min(...values), max = Math.max(...values), span = (max - min) || 1;
      const pts = values.map((v, i) => [inset + (i / (values.length - 1)) * (w - inset * 2), inset + (1 - (v - min) / span) * (h - inset * 2)]);
      const grad = ctx.createLinearGradient(0, 0, 0, h);
      grad.addColorStop(0, hexA(color, 0.42)); grad.addColorStop(1, hexA(color, 0));
      ctx.beginPath(); ctx.moveTo(pts[0][0], h);
      pts.forEach(p => ctx.lineTo(p[0], p[1]));
      ctx.lineTo(pts[pts.length - 1][0], h); ctx.closePath();
      ctx.fillStyle = grad; ctx.fill();
      ctx.beginPath(); pts.forEach((p, i) => i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]));
      ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.lineJoin = 'round'; ctx.lineCap = 'round'; ctx.stroke();
      const last = pts[pts.length - 1];
      ctx.beginPath(); ctx.arc(last[0], last[1], 3.5, 0, Math.PI * 2); ctx.fillStyle = color; ctx.fill();
    });
  }

  function axisFont() { return '12px "Segoe UI Variable Text", "Segoe UI", sans-serif'; }

  function availability(canvas, getValues, target) {
    return bind(canvas, () => {
      const values = getValues();
      const { ctx, w, h } = fitCanvas(canvas);
      if (w < 40 || h < 40) return;
      const accent = css('--accent'), grid = css('--chart-grid'), sec = css('--text-secondary'), tert = css('--text-tertiary');
      const L = 44, R = 12, T = 12, B = 28;
      const pw = w - L - R, ph = h - T - B;
      const yMin = 94, yMax = 100;
      const y = v => T + (1 - (v - yMin) / (yMax - yMin)) * ph;
      const x = i => L + (i / (values.length - 1)) * pw;
      ctx.font = axisFont(); ctx.textBaseline = 'middle';
      for (let v = yMin; v <= yMax; v += 2) {
        ctx.strokeStyle = grid; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(L, Math.round(y(v)) + .5); ctx.lineTo(w - R, Math.round(y(v)) + .5); ctx.stroke();
        ctx.fillStyle = tert; ctx.textAlign = 'right'; ctx.fillText(v + '%', L - 10, y(v));
      }
      ctx.textAlign = 'center'; ctx.textBaseline = 'top';
      [[0, '30 d ago'], [Math.floor((values.length - 1) / 2), '15 d'], [values.length - 1, 'Today']].forEach(([i, t]) => {
        ctx.fillStyle = tert; ctx.textAlign = i === 0 ? 'left' : i === values.length - 1 ? 'right' : 'center'; ctx.fillText(t, x(i), h - B + 10);
      });
      // target
      ctx.setLineDash([6, 5]); ctx.strokeStyle = sec; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.moveTo(L, y(target)); ctx.lineTo(w - R, y(target)); ctx.stroke(); ctx.setLineDash([]);
      // area
      const grad = ctx.createLinearGradient(0, T, 0, T + ph);
      grad.addColorStop(0, hexA(accent, 0.32)); grad.addColorStop(1, hexA(accent, 0));
      ctx.beginPath(); ctx.moveTo(x(0), T + ph);
      values.forEach((v, i) => ctx.lineTo(x(i), y(v)));
      ctx.lineTo(x(values.length - 1), T + ph); ctx.closePath(); ctx.fillStyle = grad; ctx.fill();
      ctx.beginPath(); values.forEach((v, i) => i ? ctx.lineTo(x(i), y(v)) : ctx.moveTo(x(i), y(v)));
      ctx.strokeStyle = accent; ctx.lineWidth = 2.5; ctx.lineJoin = 'round'; ctx.stroke();
      // today marker
      const li = values.length - 1, lx = x(li), ly = y(values[li]);
      ctx.beginPath(); ctx.arc(lx, ly, 9, 0, Math.PI * 2); ctx.fillStyle = hexA(accent, 0.25); ctx.fill();
      ctx.beginPath(); ctx.arc(lx, ly, 4.5, 0, Math.PI * 2); ctx.fillStyle = accent; ctx.fill();
    });
  }

  function vibration(canvas, opts) {
    // opts: { values, average, limit, attentionFrom, getMarker() }
    const c = bind(canvas, () => {
      const val = v => typeof v === 'function' ? v() : v;
      const values = val(opts.values), average = val(opts.average);
      const { limit, attentionFrom } = opts;
      const marker = opts.getMarker ? opts.getMarker() : values.length - 1;
      const { ctx, w, h } = fitCanvas(canvas);
      if (w < 40 || h < 40) return;
      const accent = css('--accent'), att = css('--attention'), crit = css('--critical'), grid = css('--chart-grid');
      const sec = css('--text-secondary'), tert = css('--text-tertiary'), neutral = css('--neutral');
      const L = 36, R = 8, T = 10, B = 24;
      const pw = w - L - R, ph = h - T - B;
      const yMax = 2.6;
      const y = v => T + (1 - v / yMax) * ph;
      const x = i => L + (i / (values.length - 1)) * pw;
      ctx.font = axisFont();
      ctx.textBaseline = 'middle';
      for (let v = 0; v <= 2.5 + 1e-6; v += 0.5) {
        ctx.strokeStyle = grid; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(L, Math.round(y(v)) + .5); ctx.lineTo(w - R, Math.round(y(v)) + .5); ctx.stroke();
        ctx.fillStyle = tert; ctx.textAlign = 'right'; ctx.fillText(v.toFixed(1), L - 8, y(v));
      }
      ctx.textBaseline = 'top';
      [[0, '1'], [24, '25'], [values.length - 1, '50']].forEach(([i, t]) => {
        ctx.fillStyle = tert; ctx.textAlign = i === 0 ? 'left' : i === values.length - 1 ? 'right' : 'center'; ctx.fillText(t, x(i), h - B + 8);
      });
      // fleet band
      ctx.beginPath();
      average.forEach((v, i) => i ? ctx.lineTo(x(i), y(v + 0.22)) : ctx.moveTo(x(i), y(v + 0.22)));
      for (let i = average.length - 1; i >= 0; i--) ctx.lineTo(x(i), y(Math.max(0, average[i] - 0.22)));
      ctx.closePath(); ctx.fillStyle = hexA(neutral, 0.16); ctx.fill();
      ctx.setLineDash([2, 4]); ctx.strokeStyle = sec; ctx.lineWidth = 1.5; ctx.beginPath();
      average.forEach((v, i) => i ? ctx.lineTo(x(i), y(v)) : ctx.moveTo(x(i), y(v))); ctx.stroke();
      // limit
      ctx.setLineDash([6, 5]); ctx.strokeStyle = crit; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.moveTo(L, y(limit)); ctx.lineTo(w - R, y(limit)); ctx.stroke(); ctx.setLineDash([]);
      // series
      ctx.lineWidth = 2.5; ctx.lineJoin = 'round'; ctx.lineCap = 'round';
      ctx.beginPath(); for (let i = 0; i <= attentionFrom; i++) i ? ctx.lineTo(x(i), y(values[i])) : ctx.moveTo(x(i), y(values[i]));
      ctx.strokeStyle = accent; ctx.stroke();
      ctx.beginPath(); for (let i = attentionFrom; i < values.length; i++) i === attentionFrom ? ctx.moveTo(x(i), y(values[i])) : ctx.lineTo(x(i), y(values[i]));
      ctx.strokeStyle = att; ctx.stroke();
      // scrub marker
      const mx = x(marker), my = y(values[marker]);
      const mc = values[marker] >= limit ? att : accent;
      ctx.strokeStyle = hexA(mc, 0.5); ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(Math.round(mx) + .5, T); ctx.lineTo(Math.round(mx) + .5, T + ph); ctx.stroke();
      ctx.beginPath(); ctx.arc(mx, my, 9, 0, Math.PI * 2); ctx.fillStyle = hexA(mc, 0.25); ctx.fill();
      ctx.beginPath(); ctx.arc(mx, my, 4.5, 0, Math.PI * 2); ctx.fillStyle = mc; ctx.fill();
    });
    return c;
  }

  window.Charts = { sparkline, availability, vibration, hexA };
})();
