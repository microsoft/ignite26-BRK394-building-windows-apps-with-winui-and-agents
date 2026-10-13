(function () {
  'use strict';
  const { h, icon } = window.UI;

  const CSS = `
.ac { position: absolute; inset: 0; overflow: hidden; }
.ac > canvas.holo { position: absolute; inset: 0; width: 100%; height: 100%; outline: none; }
.ac-overlay { position: absolute; inset: 0; display: grid; grid-template-columns: 340px 1fr 440px; grid-template-rows: auto 1fr auto;
  column-gap: 24px; row-gap: 20px; padding: 28px 40px; pointer-events: none; }
.ac-overlay .glass, .ac-overlay .dock, .ac-overlay button { pointer-events: auto; }
.ac-head { grid-column: 1 / 3; grid-row: 1; display: flex; flex-direction: column; gap: 6px; }
.ac-tail { font-family: var(--font-mono); font-size: 56px; line-height: 64px; font-weight: 600; letter-spacing: -0.01em; margin: 0; }
.ac-vitals { grid-column: 1; grid-row: 2; align-self: start; display: flex; flex-direction: column; gap: 16px; }
.ac-stats { display: grid; grid-template-columns: 1fr 1fr; gap: 16px 12px; }
.ac-stat .v { font-size: 20px; line-height: 28px; font-weight: 600; font-family: var(--font-display); }
.ac-stat .v.mono { font-family: var(--font-mono); }
.ac-sep { height: 1px; background: var(--glass-stroke); }
.ac-row { display: flex; justify-content: space-between; align-items: baseline; font-size: 13px; line-height: 20px; }
.ac-row .k { color: var(--text-secondary); }
.ac-row .v { font-family: var(--font-mono); font-weight: 600; }
.ac-row .v.att { color: var(--attention); }
.ac-vib { grid-column: 3; grid-row: 1 / 3; align-self: start; display: flex; flex-direction: column; gap: 12px; }
.ac-vib .title { font-family: var(--font-display); font-size: 20px; line-height: 28px; font-weight: 600; }
.ac-cur { display: flex; align-items: baseline; gap: 8px; }
.ac-cur .n { font-family: var(--font-mono); font-size: 30px; line-height: 36px; font-weight: 600; }
.ac-cur .n.att { color: var(--attention); }
.ac-cur .u { font-size: 13px; color: var(--text-secondary); }
.ac-vib canvas { width: 100%; height: 240px; display: block; }
.ac-legend { display: flex; gap: 16px; font-size: 12px; color: var(--text-secondary); flex-wrap: wrap; }
.ac-legend span { display: inline-flex; align-items: center; gap: 6px; }
.ac-legend i { width: 14px; height: 3px; border-radius: 2px; background: var(--accent); display: inline-block; }
.ac-legend i.avg { background: var(--text-secondary); opacity: .7; }
.ac-legend i.lim { background: transparent; border-top: 2px dashed var(--attention); height: 0; }
.ac-part { display: flex; flex-direction: column; gap: 6px; }
.ac-part .pt { font-size: 15px; line-height: 22px; font-weight: 600; }
.ac-part .pt.on { color: var(--accent); }
.ac-part .pb { font-size: 13px; line-height: 20px; color: var(--text-secondary); }
.ac-part .btn { align-self: flex-start; margin-top: 6px; }
.dock { grid-column: 1 / 4; grid-row: 3; justify-self: center; display: flex; align-items: center; gap: 8px; padding: 10px 14px;
  border-radius: 24px; background: var(--glass); border: 1px solid var(--glass-stroke); backdrop-filter: blur(24px); box-shadow: 0 12px 32px #0005; }
.dock .div { width: 1px; height: 24px; background: var(--glass-stroke); margin: 0 6px; }
.dock .scrub { display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--text-secondary); font-family: var(--font-mono); }
.dock .scrub .icon { font-size: 14px; color: var(--text-secondary); }
.dock input[type=range] { width: 360px; accent-color: var(--accent); }
.dock .btn.icon-only { border-radius: 16px; }
`;

  const PART_COMPONENT = { Fan: 'Fan blade', Turbine: 'HPT blade', Compressor: 'Compressor', Nacelle: 'Nacelle', None: 'Engine 2' };
  const PART_LABEL = { Nacelle: 'Nacelle', Fan: 'Fan', Compressor: 'Compressor', Turbine: 'Turbine' };
  const PART_TEXT = {
    Nacelle: ['Nacelle & thrust reverser', 'Inlet and cowl clean on walkaround. No acoustic panel damage. 1 open work order.'],
    Fan: ['Fan · stage 1 blade 14', 'Leading-edge nick suspected — the likely source of the N1 vibration spike. Borescope inspection required before 06:15.'],
    Compressor: ['High-pressure compressor', 'Borescope history clean. Trend within the fleet band for the last 50 flights.'],
    Turbine: ['HP turbine', 'EGT margin 42 °C. Blade coating within limits at the last shop visit.'],
  };
  const EXPLODED = ['Engine 2 · exploded', '50+ LEAP-1A parts across four modules. Tap a chip to isolate the nacelle, fan, compressor or turbine; tap empty space or press Esc to collapse.'];
  const DEFAULT = ['Tap the engine to explode it', 'Tap Engine 2 or press Explode to separate 50+ LEAP-1A parts; tap a chip to isolate the nacelle, fan, compressor or turbine. Esc collapses.'];

  function ensureCss() {
    if (!document.getElementById('aircraft-css')) document.head.appendChild(h('style#aircraft-css', { text: CSS }));
  }

  function mount(root, ctx) {
    ensureCss();
    const { data } = ctx;
    const H = window.Hangar;
    const param = ctx.param || {};
    const tail = param.tail || H.HeroTail;
    const ac = () => data.fleet.find(f => f.tail === tail) || data.hero;
    const isHero = () => ac().tail === H.HeroTail;
    const offs = [];

    const page = h('div.ac');
    const canvas = h('canvas.holo#HologramCanvas', { 'aria-label': 'Engine 2 hologram' });
    page.appendChild(canvas);
    root.appendChild(page);
    const holo = new window.Hologram(canvas);
    const applyTheme = () => holo.setTheme({ light: ctx.theme.light, highContrast: ctx.theme.highContrast });
    applyTheme();

    // Header
    const eyebrow = h('div.eyebrow');
    const tailEl = h('h1.ac-tail', tail);
    const statusPill = h('span.pill-tag');
    const head = h('div.ac-head', eyebrow, tailEl, h('div', statusPill));

    // Vitals
    const stat = (k, cls) => { const v = h(`div.v${cls ? '.' + cls : ''}`); return [h('div.ac-stat', h('div.eyebrow', k), v), v]; };
    const [sCycles, vCycles] = stat('CYCLES');
    const [sHours, vHours] = stat('FLIGHT HOURS');
    const [sCheck, vCheck] = stat('NEXT CHECK');
    const [sStation, vStation] = stat('STATION');
    const vibLast = h('span.v');
    const woBtnText = h('span');
    const woBtn = h('button.btn#EngineWorkOrdersButton', {
      onClick: () => ctx.navigate('workorders', { tail, component: PART_COMPONENT[holo.selectedPart] }),
    }, icon('E9D5'), woBtnText);
    const vitals = h('div.glass.ac-vitals',
      h('div.ac-stats', sCycles, sHours, sCheck, sStation),
      h('div.ac-sep'),
      h('div.eyebrow', 'ENGINE 2 · LEAP-1A'),
      h('div', h('div.ac-row', h('span.k', 'N1 vibration · last'), vibLast),
        h('div.ac-row', h('span.k', 'Limit'), h('span.v', `${H.VibrationThreshold.toFixed(2)} ips`)),
        h('div.ac-row', h('span.k', 'ESN'), h('span.v', '598-214'))),
      woBtn);

    // Vibration card
    const curN = h('span.n');
    const curU = h('span.u');
    const chartCv = h('canvas#VibrationChart');
    const partTitle = h('div.pt');
    const partBody = h('div.pb');
    const boreBtn = h('button.btn.accent#BorescopeButton', {
      onClick: () => { holo.explodeEngine(); holo.select('Fan'); holo.diveToFault(); },
    }, icon('E890'), 'Borescope blade 14');
    const vib = h('div.glass.ac-vib',
      h('div', h('div.eyebrow', 'ENGINE 2 · N1 VIBRATION'), h('div.title', 'Last 50 flights')),
      h('div.ac-cur', curN, curU),
      chartCv,
      h('div.ac-legend',
        h('span', h('i'), `${H.HeroFlight} Eng 2`),
        h('span', h('i.avg'), 'Fleet average'),
        h('span', h('i.lim'), `Limit ${H.VibrationThreshold}`)),
      h('div.ac-sep'),
      h('div.ac-part', h('div.eyebrow', 'SELECTED PART'), partTitle, partBody, boreBtn));

    // Dock
    const explodeChip = h('button.chip#ExplodeChip', {
      tip: 'Explode Engine 2', onClick: () => holo.setExploded(!holo.isExploded),
    }, icon('F156'), 'Explode');
    const partChips = {};
    Object.keys(PART_LABEL).forEach(p => {
      partChips[p] = h(`button.chip#PartChip_${p}`, {
        tip: `Isolate ${PART_LABEL[p].toLowerCase()}`,
        onClick: () => {
          const next = holo.selectedPart === p ? 'None' : p;
          if (next !== 'None' && !holo.isExploded) holo.explodeEngine();
          holo.select(next);
        },
      }, PART_LABEL[p]);
    });
    const range = h('input#FlightScrubber', { type: 'range', min: '0', max: '49', step: '1', value: '49', 'aria-label': 'Flight' });
    const resetBtn = h('button.btn.icon-only#ResetViewButton', { tip: 'Reset view', onClick: () => holo.resetView() }, icon('E72C'));
    const dock = h('div.dock',
      explodeChip, ...Object.values(partChips),
      h('div.div'),
      h('div.scrub', icon('E81C'), h('span', 'FLT 1'), range, h('span', 'FLT 50')),
      h('div.div'),
      resetBtn);

    const overlay = h('div.ac-overlay', head, vitals, vib, dock);
    page.appendChild(overlay);

    let marker = 49;
    const chart = window.Charts.vibration(chartCv, {
      values: () => data.vibration, average: () => data.vibrationAverage,
      limit: H.VibrationThreshold, attentionFrom: 46, getMarker: () => marker,
    });

    function heatFor(v) { const max = data.vibration.reduce((m, x) => Math.max(m, x), 0); return Math.min(1, Math.max(0, (v - 1) / (max - 1))); }

    function renderVib() {
      const v = data.vibration[marker];
      const att = v >= H.VibrationThreshold;
      curN.textContent = v.toFixed(2);
      curN.className = `n${att ? ' att' : ''}`;
      curU.textContent = `ips · flight ${marker + 1}`;
      const last = data.vibration[data.vibration.length - 1];
      vibLast.textContent = `${last.toFixed(2)} ips`;
      vibLast.className = `v${last >= H.VibrationThreshold ? ' att' : ''}`;
      chart.redraw();
    }

    function renderPart() {
      const p = holo.selectedPart;
      const [t, b] = p !== 'None' ? PART_TEXT[p] : holo.isExploded ? EXPLODED : DEFAULT;
      partTitle.textContent = t;
      partTitle.className = `pt${p !== 'None' ? ' on' : ''}`;
      partBody.textContent = b;
      boreBtn.style.display = p === 'Fan' && data.hero.status !== 'Healthy' && isHero() ? '' : 'none';
      woBtnText.textContent = p === 'None' ? 'Engine 2 work orders' : `${PART_COMPONENT[p]} work orders`;
      explodeChip.classList.toggle('on', holo.isExploded);
      explodeChip.setAttribute('aria-pressed', String(holo.isExploded));
      Object.entries(partChips).forEach(([k, el]) => { el.classList.toggle('on', k === p); el.setAttribute('aria-pressed', String(k === p)); });
    }

    function renderHeader() {
      const a = ac();
      eyebrow.textContent = `${a.flight} · ${a.type} · ${a.station} → JFK ${a.nextDeparture}`;
      const healthy = a.status === 'Healthy';
      if (isHero()) {
        statusPill.className = `pill-tag ${healthy ? 'healthy' : 'attention'}`;
        statusPill.textContent = healthy ? 'Released to service' : 'Engine 2 vibration · hold';
      } else {
        const st = { Healthy: ['healthy', 'Serviceable'], Attention: ['attention', 'Needs attention'], Maintenance: ['neutral', 'In check'], Aog: ['critical', 'AOG'] }[a.status];
        statusPill.className = `pill-tag ${st[0]}`;
        statusPill.textContent = a.statusNote ? `${st[1]} · ${a.statusNote}` : st[1];
      }
      vCycles.textContent = window.UI.fmtInt(a.cycles);
      vHours.textContent = window.UI.fmtInt(a.flightHours);
      vCheck.textContent = `${a.daysToNextCheck} d`;
      vStation.textContent = 'Hangar 3 · Bay 2';
      holo.setStatus(isHero() ? data.hero.status : a.status);
    }

    range.addEventListener('input', () => {
      marker = Number(range.value);
      holo.setHeat(heatFor(data.vibration[marker]));
      renderVib();
    });

    offs.push(
      holo.on('partSelected', renderPart),
      holo.on('explodedChanged', renderPart),
      holo.on('faultInvoked', () => { holo.select('Fan'); holo.diveToFault(); }),
      holo.on('diveCompleted', () => ctx.navigate('inspections', { tail: H.HeroTail, finding: 'Fan blade 14 · leading-edge nick' })),
      data.on('reset', () => {
        marker = 49; range.value = '49';
        holo.collapseEngine(); holo.select('None'); holo.resetView();
        renderHeader(); renderVib(); renderPart();
        holo.setHeat(heatFor(data.vibration[marker]));
      }),
      data.on('heroStatusChanged', () => {
        renderHeader(); renderPart();
        if (isHero() && data.hero.status === 'Healthy') holo.celebrate();
      }),
      ctx.theme.onChanged(() => { applyTheme(); chart.redraw(); }),
    );

    const onKey = e => {
      if (e.key === 'Escape' && document.activeElement !== canvas && !e.defaultPrevented) {
        if (holo.selectedPart !== 'None') holo.select('None'); else if (holo.isExploded) holo.collapseEngine();
      }
    };
    document.addEventListener('keydown', onKey);

    const ro = new ResizeObserver(() => chart.redraw());
    ro.observe(vib);

    renderHeader();
    renderVib();
    renderPart();
    holo.setHeat(heatFor(data.vibration[marker]));
    holo.start();
    if (param.part && param.part !== 'None') { holo.explodeEngine(); holo.select(param.part); }
    requestAnimationFrame(() => canvas.focus({ preventScroll: true }));

    return {
      unmount() {
        document.removeEventListener('keydown', onKey);
        ro.disconnect();
        offs.forEach(o => o && o());
        chart.destroy();
        holo.destroy();
      },
    };
  }

  (window.Pages ||= {}).aircraft = { fullBleed: true, mount };
})();
