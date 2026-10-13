'use strict';
window.addEventListener('error', (e) => { window.__lastErr = e.message + ' @' + e.filename + ':' + e.lineno; console.error('[renderer error]', window.__lastErr); });
window.addEventListener('unhandledrejection', (e) => console.error('[renderer rejection]', String(e.reason && e.reason.stack || e.reason)));
(function () {
  const { h, icon, theme } = window.UI;
  const H = window.Hangar;
  const data = H.data;
  const host = window.host || null;
  const t0 = performance.now();

  // ---------- Title bar ----------
  const searchInput = h('input.text-box#SearchBox', { type: 'text', placeholder: 'Search tail, part, or work order', 'aria-label': 'Search', autocomplete: 'off', spellcheck: 'false' });
  const pop = h('div.asb-pop.hidden', { role: 'listbox' });
  const asb = h('div.asb', searchInput, icon('E721'), pop);
  const clock = h('span.clock#Clock');
  const depIcon = icon('E7BA');
  const depFlight = h('span.flight');
  const depCount = h('span.countdown');
  const depNote = h('span');
  const depChip = h('button.dep-chip#DepartureChip', { tip: 'Open CA 2207', onClick: () => navigate('aircraft', { tail: H.HeroTail }) },
    depIcon, depFlight, depCount, depNote);
  const avatar = h('div.avatar', { tip: 'Maya Okafor · Shift lead' }, 'MO');
  document.getElementById('titlebar').append(
    h('div.brand', icon('E709'), h('span.brand-name', 'HANGAR'), h('span.brand-sub', 'Contoso Aero · Night shift')),
    h('div.tb-center', asb),
    h('div.tb-right', clock, depChip, avatar));

  const clockStart = H.Now + 48000;
  const wallStart = Date.now();
  function tick() {
    const now = new Date(clockStart + (Date.now() - wallStart));
    clock.textContent = `${H.fmtDate ? H.fmtDate(now) : now.toISOString().slice(0, 10)} ${H.pad2(now.getUTCHours())}:${H.pad2(now.getUTCMinutes())}:${H.pad2(now.getUTCSeconds())}`;
    const released = data.hero.status === 'Healthy';
    depChip.classList.toggle('released', released);
    if (released) {
      depIcon.textContent = String.fromCodePoint(0xE73E);
      depFlight.textContent = `${H.HeroFlight} · ${H.HeroTail}`;
      depCount.textContent = '';
      depNote.textContent = 'Released to service';
    } else {
      depIcon.textContent = String.fromCodePoint(0xE7BA);
      const left = Math.max(0, Math.floor((H.Departure - now.getTime()) / 1000));
      depFlight.textContent = `${H.HeroFlight} · ${H.HeroTail}`;
      depCount.textContent = `${Math.floor(left / 3600)}h ${H.pad2(Math.floor(left / 60) % 60)}m ${H.pad2(left % 60)}s`;
      depNote.textContent = 'Eng 2 vib';
    }
  }
  tick();
  setInterval(tick, 1000);

  // ---------- Search ----------
  let results = [], selIdx = -1;
  const norm = s => String(s).toLowerCase().replace(/\s+/g, '');
  function runSearch(q) {
    const n = norm(q);
    if (!n) return [];
    const ac = data.fleet.filter(a => norm(a.tail).includes(n) || norm(a.flight).includes(n)).slice(0, 4)
      .map(a => ({ kind: 'aircraft', glyph: 'F158', t: a.tail, s: `${a.flight} · ${a.type} · ${a.statusNote || a.status}`, a }));
    const wo = [];
    for (const w of data.workOrders) {
      if (norm(w.number).includes(n) || norm(w.numberText).includes(n) || norm(w.partNumber).includes(n) || norm(w.component).includes(n)) {
        wo.push({ kind: 'wo', glyph: 'E9F9', t: `${w.numberText} · ${w.component}`, s: `${w.tail} · ${w.partNumber} · ${w.statusText}`, w });
        if (wo.length >= 6) break;
      }
    }
    return ac.concat(wo);
  }
  function renderPop() {
    pop.replaceChildren(...results.map((r, i) => h(`div.asb-item${i === selIdx ? '.sel' : ''}`, {
      role: 'option', onPointerdown: e => { e.preventDefault(); submit(r); },
    }, icon(r.glyph), h('div', h('div.t', r.t), h('div.s', r.s)))));
    pop.classList.toggle('hidden', !results.length);
  }
  function closePop() { results = []; selIdx = -1; renderPop(); }
  function submit(r) {
    const q = searchInput.value.trim();
    if (r && r.kind === 'aircraft') navigate('aircraft', { tail: r.a.tail });
    else if (r && r.kind === 'wo') navigate('workorders', r.w.numberText);
    else if (q) navigate('workorders', q);
    searchInput.value = '';
    closePop();
    searchInput.blur();
  }
  searchInput.addEventListener('input', () => { results = runSearch(searchInput.value); selIdx = -1; renderPop(); });
  searchInput.addEventListener('blur', () => setTimeout(closePop, 120));
  searchInput.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown' && results.length) { selIdx = (selIdx + 1) % results.length; renderPop(); e.preventDefault(); }
    else if (e.key === 'ArrowUp' && results.length) { selIdx = (selIdx - 1 + results.length) % results.length; renderPop(); e.preventDefault(); }
    else if (e.key === 'Enter') { submit(results[selIdx] || (results.length === 1 ? results[0] : null)); e.preventDefault(); }
    else if (e.key === 'Escape') { searchInput.value = ''; closePop(); searchInput.blur(); e.preventDefault(); }
  });

  // ---------- Rail ----------
  const NAV = [
    { page: 'fleet', label: 'Fleet', glyph: 'E709' },
    { page: 'aircraft', label: 'Aircraft', glyph: 'F158' },
    { page: 'workorders', label: 'Work Orders', glyph: 'E9F9' },
    { page: 'inspections', label: 'Inspections', glyph: 'EDC6' },
  ];
  const rail = document.getElementById('rail');
  const railItems = {};
  let badge;
  for (const n of NAV) {
    const ic = icon(n.glyph);
    if (n.page === 'workorders') { badge = h('span.badge#WorkOrderBadge', { 'aria-hidden': 'true' }); ic.appendChild(badge); }
    const id = 'Nav' + n.label.replace(/\s/g, '');
    const el = h(`button.rail-item#${id}`, { tip: n.label, 'aria-label': n.label, onClick: () => navigate(n.page) },
      h('span.pill'), ic, h('span.lbl', n.label));
    railItems[n.page] = el;
    rail.appendChild(el);
  }
  function updateBadge() {
    const c = data.openWorkOrderCount;
    railItems.workorders.setAttribute('aria-label', `Work Orders, ${c} open`);
    badge.classList.toggle('hidden', !c);
  }
  updateBadge();
  data.on('heroStatusChanged', updateBadge);
  data.on('reset', updateBadge);

  // ---------- Navigation ----------
  const content = document.getElementById('content');
  let current = null, currentName = null;
  function navigate(name, param) {
    const def = window.Pages[name];
    if (!def) return;
    if (current) { try { current.inst.unmount(); } catch (e) { console.error(e); } current.el.remove(); }
    document.body.classList.toggle('fullbleed', !!def.fullBleed);
    for (const [k, el] of Object.entries(railItems)) {
      el.classList.toggle('sel', k === name);
      el.setAttribute('aria-current', k === name ? 'page' : 'false');
    }
    const el = h('div.page');
    content.appendChild(el);
    const ctx = { data, navigate, param, theme, showInfo };
    const inst = def.mount(el, ctx) || { unmount() {} };
    current = { el, inst };
    currentName = name;
  }

  // ---------- InfoBar ----------
  const infobar = document.getElementById('infobar');
  let infoTimer;
  function showInfo(title, msg) {
    infobar.replaceChildren(icon('E946'), h('div', h('b', title), msg));
    infobar.classList.remove('hidden');
    infobar.style.animation = 'none'; void infobar.offsetWidth; infobar.style.animation = '';
    clearTimeout(infoTimer);
    infoTimer = setTimeout(() => infobar.classList.add('hidden'), 2500);
  }

  // ---------- Shortcuts ----------
  document.addEventListener('keydown', e => {
    const k = e.key.toLowerCase();
    if (e.ctrlKey && e.shiftKey && k === 'l') {
      const light = document.documentElement.classList.toggle('light');
      if (host) host.setTheme(light ? 'light' : 'dark');
      theme._raise();
      e.preventDefault();
    } else if (e.ctrlKey && e.shiftKey && k === 'd') {
      data.resetDemo();
      showInfo('Demo reset', 'CA 2207 is back to its pre-inspection state.');
      e.preventDefault();
    } else if (e.ctrlKey && !e.shiftKey && k === 'k') {
      searchInput.focus(); searchInput.select();
      e.preventDefault();
    }
  });

  if (host && host.initialTheme === 'light') {
    document.documentElement.classList.add('light');
    host.setTheme('light');
  }
  navigate(host && host.initialPage && railItems[host.initialPage] ? host.initialPage : 'fleet');
  window.App = { navigate, get page() { return currentName; } };

  // ---------- Benchmark hooks ----------
  const afterPaint = () => new Promise(r => {
    requestAnimationFrame(() => requestAnimationFrame(r));
    setTimeout(r, 500);
  });
  if (host && host.bench) {
    afterPaint().then(async () => {
      host.ready({ firstPaintMs: performance.now() - t0, sinceNavStart: performance.now() });
      if (host.autoNav) {
        await new Promise(r => setTimeout(r, 1500));
        const order = ['aircraft', 'workorders', 'inspections', 'fleet'];
        for (let round = 0; round < 3; round++) {
          for (const p of order) {
            const s = performance.now();
            railItems[p].click();
            await afterPaint();
            host.navTiming({ page: p, round, ms: performance.now() - s });
            await new Promise(r => setTimeout(r, 700));
          }
        }
        host.navTiming({ done: true });
      }
    });
  }
})();
