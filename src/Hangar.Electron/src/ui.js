'use strict';
// Tiny DOM + theme helpers shared by all pages.
(function () {
  function h(tag, attrs, ...children) {
    const m = tag.match(/^[^.#]*/);
    const el = document.createElement(m[0] || 'div');
    const classes = [];
    for (const [, kind, val] of tag.slice(m[0].length).matchAll(/([.#])([^.#]+)/g)) {
      if (kind === '#') el.id = val; else classes.push(val);
    }
    if (classes.length) el.className = classes.join(' ');
    if (attrs != null && (typeof attrs !== 'object' || attrs instanceof Node || Array.isArray(attrs))) {
      children.unshift(attrs);
      attrs = null;
    }
    if (attrs) {
      for (const [k, v] of Object.entries(attrs)) {
        if (v == null || v === false) continue;
        if (k === 'class') el.className += (el.className ? ' ' : '') + v;
        else if (k === 'style' && typeof v === 'object') {
          for (const [sk, sv] of Object.entries(v)) sk.startsWith('--') ? el.style.setProperty(sk, sv) : (el.style[sk] = sv);
        }
        else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
        else if (k === 'text') el.textContent = v;
        else if (k === 'html') el.innerHTML = v;
        else if (k === 'tip') el.dataset.tip = v;
        else el.setAttribute(k, v === true ? '' : v);
      }
    }
    for (const c of children.flat(Infinity)) {
      if (c == null || c === false) continue;
      el.appendChild(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return el;
  }
  const icon = (glyph, extra) => h('i.icon', { class: extra, 'aria-hidden': 'true' }, String.fromCodePoint(parseInt(glyph, 16)));
  const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  const initials = name => name.split(' ').map(p => p[0]).join('').slice(0, 2).toUpperCase();
  const fmtInt = n => n.toLocaleString('en-US');

  // Theme bus — pages subscribe to redraw canvases.
  const themeListeners = new Set();
  const theme = {
    get light() { return document.documentElement.classList.contains('light'); },
    get highContrast() { return matchMedia('(forced-colors: active)').matches; },
    onChanged(fn) { themeListeners.add(fn); return () => themeListeners.delete(fn); },
    _raise() { for (const f of [...themeListeners]) f(theme); },
  };
  matchMedia('(forced-colors: active)').addEventListener('change', () => theme._raise());

  // Global hover tooltip for any [data-tip].
  let tipEl, tipTimer, tipTarget;
  document.addEventListener('pointerover', e => {
    const t = e.target.closest && e.target.closest('[data-tip]');
    if (t === tipTarget) return;
    tipTarget = t; clearTimeout(tipTimer); if (tipEl) tipEl.remove(), tipEl = null;
    if (!t) return;
    tipTimer = setTimeout(() => {
      tipEl = h('div', { id: 'tooltip', role: 'tooltip' }, t.dataset.tip);
      document.body.appendChild(tipEl);
      const r = t.getBoundingClientRect(), tr = tipEl.getBoundingClientRect();
      let x = r.left + r.width / 2 - tr.width / 2, y = r.bottom + 8;
      if (y + tr.height > innerHeight - 4) y = r.top - tr.height - 8;
      x = Math.max(4, Math.min(innerWidth - tr.width - 4, x));
      tipEl.style.left = x + 'px'; tipEl.style.top = y + 'px';
    }, 500);
  });
  document.addEventListener('pointerdown', () => { clearTimeout(tipTimer); if (tipEl) tipEl.remove(), tipEl = null; tipTarget = null; });

  // HiDPI canvas helper: sizes the backing store to the element and returns a 2D context in CSS px.
  function fitCanvas(canvas) {
    const dpr = devicePixelRatio || 1;
    const w = canvas.clientWidth, hh = canvas.clientHeight;
    if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(hh * dpr)) {
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(hh * dpr);
    }
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, hh);
    return { ctx, w, h: hh };
  }

  window.UI = { h, icon, css, initials, fmtInt, theme, fitCanvas };
})();
