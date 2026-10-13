/* Fleet overview — port of Hangar/Views/FleetPage. */
(() => {
  const { h, icon, fmtInt } = UI;
  const PLANE = 'M50,4 C54,4 56,10 56,18 L56,38 L94,58 L94,65 L56,54 L56,78 L68,88 L68,94 L50,89 L32,94 L32,88 L44,78 L44,54 L6,65 L6,58 L44,38 L44,18 C44,10 46,4 50,4 Z';
  const STATUS = {
    Healthy: { cls: 'healthy', text: 'Serviceable' },
    Attention: { cls: 'attention', text: 'Needs attention' },
    Maintenance: { cls: 'neutral', text: 'In check' },
    Aog: { cls: 'critical', text: 'AOG' },
  };

  function ensureCss() {
    if (document.getElementById('fleet-css')) return;
    document.head.appendChild(h('style#fleet-css', { text: `
.fleet { padding: 28px 40px 40px; display: flex; flex-direction: column; gap: 24px; }
.fleet-head { display: flex; align-items: flex-end; gap: 24px; }
.fleet-head .lead { flex: 1; display: flex; flex-direction: column; gap: 8px; min-width: 0; }
.fleet-head .sub { font-size: 16px; line-height: 24px; color: var(--text-secondary); max-width: 760px; }
.kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; }
.kpi { padding: 20px 20px 12px 18px; display: flex; flex-direction: column; gap: 4px; }
.kpi canvas { width: 100%; height: 56px; display: block; margin-top: 6px; }
.avail-card { display: flex; flex-direction: column; gap: 12px; }
.avail-head { display: flex; align-items: flex-start; gap: 16px; }
.avail-head .t { flex: 1; }
.legend { display: flex; gap: 16px; align-items: center; font-size: 12px; color: var(--text-secondary); }
.legend i { display: inline-block; width: 16px; height: 2px; vertical-align: middle; margin-right: 6px; background: var(--accent); }
.legend i.dash { background: none; border-top: 2px dashed var(--text-tertiary); height: 0; }
.legend i.attn { background: var(--attention); }
.legend i.avg { background: var(--text-tertiary); }
.avail-card canvas { width: 100%; height: 260px; display: block; }
.fleet-sec-head { display: flex; align-items: baseline; gap: 20px; flex-wrap: wrap; }
.counts { display: flex; gap: 16px; font-size: 13px; color: var(--text-secondary); }
.counts span::before { content: ""; display: inline-block; width: 8px; height: 8px; border-radius: 4px; margin-right: 6px; background: var(--c); }
.tiles { display: grid; gap: 12px; }
.tile {
  position: relative; height: 148px; overflow: hidden; padding: 16px; display: flex; flex-direction: column; gap: 2px;
  cursor: pointer; text-align: left; transition: background .15s, transform .15s;
}
.tile:hover { background: var(--surface-hover); }
.tile:active { transform: scale(.985); }
.tile .row { display: flex; align-items: center; gap: 8px; }
.tile .tl { font-family: var(--font-mono); font-size: 18px; font-weight: 600; line-height: 24px; flex: 1; }
.ring { width: 18px; height: 18px; border-radius: 9px; border: 1.5px solid var(--c); display: grid; place-items: center; }
.ring::after { content: ""; width: 8px; height: 8px; border-radius: 4px; background: var(--c); }
.tile .meta { font-size: 13px; color: var(--text-secondary); }
.tile .note { font-size: 12px; color: var(--text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 70%; }
.tile .foot { margin-top: auto; }
.tile svg { position: absolute; right: -6px; bottom: -8px; width: 52px; height: 52px; transform: rotate(90deg); opacity: .16; fill: var(--text-primary); }
.tile.attention svg { fill: var(--attention); opacity: .35; }
.tile.attention { border-color: var(--attention); animation: breathe 2.4s ease-in-out infinite; }
@keyframes breathe { 0%,100% { box-shadow: 0 0 0 1px var(--attention-soft); } 50% { box-shadow: 0 0 0 2px var(--attention), 0 0 18px var(--attention-soft); } }
@media (forced-colors: active) { .tile.attention { animation: none; outline: 2px solid Highlight; } }
` }));
  }

  function planeSvg() {
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 100 100');
    const p = document.createElementNS(ns, 'path');
    p.setAttribute('d', PLANE);
    svg.appendChild(p);
    return svg;
  }

  function mount(root, ctx) {
    ensureCss();
    const { data } = ctx;
    const H = window.Hangar;
    const charts = [];
    const page = h('div.fleet');
    root.appendChild(page);

    const heroText = h('h1.hero');
    const sub = h('div.sub');
    const eyebrow = h('div.eyebrow');
    const head = h('div.fleet-head',
      h('div.lead', eyebrow, heroText, sub),
      h('button.btn.accent.lg#OpenHeroButton', { onClick: () => ctx.navigate('aircraft', { tail: H.HeroTail }) },
        icon('E709'), `Open ${H.HeroFlight}`, icon('E72A')));

    const kpiDefs = [
      { k: 'FLEET AVAILABILITY', v: () => `${data.todayAvailability.toFixed(1)}%`, c: () => 'Target 98.0% · 30 days', s: () => data.availSpark, col: '--accent' },
      { k: 'AIRCRAFT ON GROUND', v: () => String(data.fleet.filter(f => f.status === 'Aog').length), c: () => 'N2231C · awaiting part at SEA', s: () => data.aogSpark, col: '--critical' },
      { k: 'OPEN WORK ORDERS', v: () => fmtInt(data.openWorkOrderCount), c: () => '14 due before first wave', s: () => data.openWoSpark, col: '--accent' },
      { k: 'ON-TIME DEPARTURE', v: () => '94%', c: () => '+1.2 pts vs. last week', s: () => data.onTimeSpark, col: '--healthy' },
    ];
    const kpiVals = [];
    const kpis = h('div.kpis', kpiDefs.map(d => {
      const val = h('div.kpi-value');
      const cv = h('canvas');
      kpiVals.push({ el: val, d });
      charts.push(Charts.sparkline(cv, d.s, d.col));
      return h('div.card.kpi', h('div.eyebrow', d.k), val, h('div.caption', d.c()), cv);
    }));

    const todayPill = h('span.pill-tag');
    const availCv = h('canvas');
    const avail = h('div.card.avail-card',
      h('div.avail-head',
        h('div.t', h('div.section-title', 'Fleet availability · 30 days'),
          h('div.caption', 'Percentage of the fleet serviceable at first departure wave')),
        h('div.legend', h('span', h('i'), 'Availability'), h('span', h('i.dash'), `Target ${H.AvailabilityTarget}%`)),
        todayPill),
      availCv);
    charts.push(Charts.availability(availCv, () => data.availability, H.AvailabilityTarget));

    const counts = h('div.counts');
    const tiles = h('div.tiles');
    const sec = h('div', { style: { display: 'flex', flexDirection: 'column', gap: '16px' } },
      h('div.fleet-sec-head', h('div.section-title', 'Fleet'), counts), tiles);

    page.append(head, kpis, avail, sec);

    function renderTiles() {
      tiles.replaceChildren(...data.fleet.map(a => {
        const st = STATUS[a.status];
        return h(`button.card.tile.${st.cls}`, {
          id: `Tile_${a.tail}`, tip: `Open ${a.tail}`, style: { '--c': `var(--${st.cls})` },
          onClick: () => ctx.navigate('aircraft', { tail: a.tail }),
        },
          h('div.row', h('span.tl', a.tail), h('span.ring')),
          h('div.meta', `${a.flight} · ${a.type}`),
          h('div.note', a.statusNote || `${a.station} · ${a.nextDeparture}`),
          h('div.foot', h(`span.pill-tag.${st.cls}`, st.text)),
          planeSvg());
      }));
    }

    function render() {
      const n = data.fleet.length;
      const need = data.fleet.filter(f => f.status === 'Attention').length;
      const healthy = data.hero.status === 'Healthy';
      eyebrow.textContent = `Night shift · SEA Hangar 3 · ${n} aircraft`;
      heroText.textContent = need === 0 ? 'All clear for first wave' : need === 1 ? '1 aircraft needs you' : `${need} aircraft need you`;
      sub.textContent = healthy
        ? `${H.HeroFlight} signed off by Maya Okafor. Engine 2 within limits — departing 06:15 as scheduled.`
        : `${H.HeroFlight} landed with an Engine 2 vibration exceedance. Next departure 06:15, 180 passengers.`;
      todayPill.className = `pill-tag ${healthy ? 'healthy' : 'attention'}`;
      todayPill.textContent = `Today ${data.todayAvailability.toFixed(1)}% · ${H.HeroFlight} ${healthy ? 'released' : 'held'}`;
      kpiVals.forEach(k => { k.el.textContent = k.d.v(); });
      const c = s => data.fleet.filter(f => f.status === s).length;
      counts.replaceChildren(
        h('span', { style: { '--c': 'var(--healthy)' } }, `${c('Healthy')} serviceable`),
        h('span', { style: { '--c': 'var(--attention)' } }, `${c('Attention')} needs attention`),
        h('span', { style: { '--c': 'var(--neutral)' } }, `${c('Maintenance')} in check`),
        h('span', { style: { '--c': 'var(--critical)' } }, `${c('Aog')} AOG`));
      renderTiles();
      charts.forEach(ch => ch.redraw());
    }

    function layout() {
      const w = tiles.clientWidth;
      const cols = Math.max(1, Math.floor((w + 12) / 244));
      tiles.style.gridTemplateColumns = `repeat(${cols}, minmax(0, 1fr))`;
    }
    const ro = new ResizeObserver(() => { layout(); charts.forEach(c => c.redraw()); });
    ro.observe(page);

    render();
    layout();
    const offs = [data.on('heroStatusChanged', render), data.on('reset', render), ctx.theme.onChanged(() => charts.forEach(c => c.redraw()))];
    return { unmount() { ro.disconnect(); offs.forEach(o => o()); charts.forEach(c => c.destroy()); } };
  }

  (window.Pages ||= {}).fleet = { fullBleed: false, mount };
})();
