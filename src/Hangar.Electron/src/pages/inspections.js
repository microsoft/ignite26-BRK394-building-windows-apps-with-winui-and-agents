'use strict';

(function () {
  const Finding = 'Fan blade 14 leading-edge nick · 0.4 mm · within limits (AMM 72-21-00)';
  const Technician = 'Maya Okafor';
  const StorageKey = 'hangar.inspections.N2207C.ink';
  const Colors = [
    { name: 'Yellow', value: '#FFD400' },
    { name: 'Cyan', value: '#3AD1F0' },
    { name: 'Red', value: '#FF5A5F' },
    { name: 'White', value: '#F2F6FA' },
  ];

  function ensureCss() {
    if (document.getElementById('inspections-css')) return;
    const link = document.createElement('link');
    link.id = 'inspections-css';
    link.rel = 'stylesheet';
    link.href = 'pages/inspections.css';
    document.head.appendChild(link);
  }

  function pill(text, kind) {
    return UI.h('span.pill-tag', { class: kind, text });
  }

  function toolButton(id, glyph, label, selected, onClick, extra) {
    return UI.h('button.ins-tool', {
      id,
      type: 'button',
      class: selected ? 'selected' : '',
      tip: label,
      'aria-label': label,
      'aria-pressed': selected ? 'true' : 'false',
      onClick,
    }, UI.icon(glyph), extra);
  }

  function metric(label, value) {
    return UI.h('div.ins-metric', null,
      UI.h('div.eyebrow', { text: label }),
      UI.h('div.mono.ins-metric-value', { text: value }));
  }

  function mount(root, ctx) {
    ensureCss();
    root.innerHTML = '';

    const page = UI.h('div.page.inspections-page');
    root.appendChild(page);

    let released = ctx.data.hero?.status === 'Healthy';
    let color = Colors[0].value;
    let drawTool = 'pen';
    let rulerOn = false;
    let colorOpen = false;
    let signing = false;
    let scopeInk = null;
    let signatureInk = null;
    const unsubs = [];

    function build() {
      const savedScope = scopeInk?.save();
      const savedSignature = signatureInk?.save();
      page.innerHTML = '';
      released = ctx.data.hero?.status === 'Healthy';

      const header = UI.h('header.ins-header', null,
        UI.h('div.eyebrow', { text: 'CA 2207 · ENGINE 2 · FAN STAGE · BORESCOPE' }),
        UI.h('div.ins-title-line', null,
          UI.h('h1.tail.ins-tail', { text: (window.Hangar && window.Hangar.HeroTail) || 'N2207C' }),
          released ? pill('AIRWORTHY · RELEASED', 'healthy') : pill('FAN BLADE 14 · HOLD', 'attention')),
        UI.h('div.caption.ins-caption', { text: 'Probe BSI-4 · 6 mm articulating · captured 02:31 by Maya Okafor' }));

      const scopeCanvas = UI.h('canvas.ins-ink-canvas', { id: 'BorescopeInk', 'aria-label': 'Borescope annotation canvas' });
      const borescope = UI.h('section.ins-scope-wrap', { 'aria-label': 'Borescope image of engine 2 fan stage' },
        UI.h('div.ins-scope', null,
          UI.h('img.ins-scope-image', { src: 'assets/borescope.png', alt: '' }),
          scopeCanvas,
          UI.h('div.ins-crosshair', { 'aria-hidden': 'true' }),
          UI.h('div.ins-scale', { 'aria-hidden': 'true' }),
          UI.h('div.mono.ins-scope-tag.top', { text: 'BSI-4 · 6 mm · 0°' }),
          UI.h('div.mono.ins-scope-tag.bottom', { text: 'LE nick · 0.38 mm' })));

      const sigCanvas = UI.h('canvas.ins-sign-canvas', { id: 'SignatureInk', 'aria-label': 'Signature ink canvas' });
      const signatureBox = UI.h('div.ins-sign-box', { id: 'SignatureBox' },
        UI.h('div.ins-sign-line'),
        UI.h('div.caption.ins-sign-hint', { text: 'Sign here' }),
        sigCanvas);

      const clearSig = UI.h('button.link', {
        id: 'ClearSignature',
        type: 'button',
        text: 'Clear',
        onClick: () => {
          if (!released && signatureInk) signatureInk.clear();
          updateReleaseState();
        },
      });

      const signButton = UI.h('button.btn.accent.ins-sign-button', {
        id: 'SignButton',
        type: 'button',
        onClick: () => signAndRelease(),
      }, UI.icon('E73E'), UI.h('span', { text: released ? 'Released to service' : 'Sign & release to service' }));

      const releaseNote = UI.h('div.caption.ins-release-note', {
        id: 'ReleaseNote',
        text: released ? `Signed by ${Technician} · ${ctx.data.hero?.statusNote || 'Released to service · 03:12'}` : 'Releases CA 2207 for the 05:52 departure',
      });

      const panel = UI.h('aside.glass.ins-panel', null,
        UI.h('div.ins-panel-head', null,
          UI.h('div.eyebrow', { text: released ? 'RELEASED · WO 2207-014' : 'FINDING · WO 2207-014' }),
          UI.h('h2.section-title', { text: 'Fan blade 14 · leading-edge nick' })),
        UI.h('div.ins-metrics', null,
          metric('DEPTH', '0.4 mm'),
          metric('LIMIT', '0.76 mm'),
          metric('AMM', '72-21-00')),
        UI.h('div.ins-limit-ok', null, UI.icon('E73E'), UI.h('span', { text: 'Within serviceable limits · blend not required' })),
        UI.h('div.ins-separator'),
        UI.h('div.ins-person', null,
          UI.h('div.avatar', { text: UI.initials(Technician) }),
          UI.h('div', null,
            UI.h('div.ins-person-name', { text: Technician }),
            UI.h('div.caption', { text: 'B1 licensed engineer · Lic. EASA-B1-48213' }))),
        UI.h('div.ins-sign-head', null,
          UI.h('div.eyebrow', { text: 'SIGNATURE' }),
          clearSig),
        signatureBox,
        signButton,
        releaseNote);

      const toolbar = buildToolbar();
      const banner = UI.h('div.ins-release-banner', { class: released ? 'show' : '' },
        UI.h('div.eyebrow', { text: 'RELEASED TO SERVICE · 03:12' }),
        UI.h('h2.page-title', { text: 'Wheels up at 05:52' }));

      page.append(header, borescope, panel, toolbar, banner);

      scopeInk?.destroy();
      signatureInk?.destroy();
      scopeInk = new InkSurface(scopeCanvas, { color, size: 3.2, highlighterSize: 18, eraserRadius: 18, ruler: rulerOn, rulerLength: 0.66 });
      scopeInk.setTool(drawTool);
      scopeInk.setColor(color);
      scopeInk.setRulerVisible(rulerOn);
      if (savedScope) scopeInk.restore(savedScope);
      signatureInk = new InkSurface(sigCanvas, { color: UI.css('--text-primary') || '#F2F6FA', size: 2.4, eraserRadius: 12 });
      signatureInk.setColor(UI.css('--text-primary') || '#F2F6FA');
      if (savedSignature) signatureInk.restore(savedSignature);
      signatureInk.on('changed', updateReleaseState);
      updateReleaseState();
    }

    function buildToolbar() {
      const swatch = UI.h('span.ins-pen-swatch', { style: { background: color } });
      const flyout = UI.h('div.ins-color-flyout', { class: colorOpen ? 'open' : '' },
        Colors.map(c => UI.h('button.ins-color', {
          type: 'button',
          title: c.name,
          'aria-label': `Ink color ${c.name}`,
          style: { '--swatch': c.value },
          class: c.value === color ? 'selected' : '',
          onClick: () => {
            color = c.value;
            colorOpen = false;
            scopeInk?.setColor(color);
            build();
          },
        })));

      const penExtra = UI.h('span.ins-pen-extra', null, swatch, UI.icon('E70D', 'ins-chevron'));
      return UI.h('div.glass.ins-toolbar', { id: 'InspectionInkToolbar', role: 'toolbar', 'aria-label': 'Annotation tools' },
        UI.h('div.ins-tool-wrap', null,
          toolButton('InkPen', 'EDFB', 'Ballpoint pen', drawTool === 'pen' && !rulerOn, () => {
            drawTool = 'pen'; colorOpen = !colorOpen; scopeInk?.setTool('pen'); scopeInk?.setColor(color); build();
          }, penExtra),
          flyout),
        toolButton('InkHighlighter', 'E7E6', 'Highlighter', drawTool === 'highlighter', () => {
          drawTool = 'highlighter'; colorOpen = false; scopeInk?.setTool('highlighter'); scopeInk?.setColor(color); build();
        }),
        toolButton('InkEraser', 'E75C', 'Stroke eraser', drawTool === 'eraser', () => {
          drawTool = 'eraser'; colorOpen = false; scopeInk?.setTool('eraser'); build();
        }),
        toolButton('InkRuler', 'ED5E', rulerOn ? 'Hide ruler' : 'Show ruler', rulerOn, () => {
          rulerOn = !rulerOn; colorOpen = false; scopeInk?.setRulerVisible(rulerOn); build();
        }),
        UI.h('div.ins-toolbar-divider'),
        UI.h('button.btn.ins-action', { id: 'SaveInk', type: 'button', onClick: saveInk }, UI.icon('E74E'), UI.h('span', { text: 'Save' })),
        UI.h('button.btn.ins-action', { id: 'RestoreInk', type: 'button', onClick: restoreInk }, UI.icon('E777'), UI.h('span', { text: 'Restore' })),
        UI.h('button.btn.icon-only.ins-action', { id: 'ClearInk', type: 'button', tip: 'Clear annotations', 'aria-label': 'Clear annotations', onClick: () => scopeInk?.clear() }, UI.icon('E711')));
    }

    function updateReleaseState() {
      const hasSignature = !!signatureInk && !signatureInk.isEmpty;
      const signButton = page.querySelector('#SignButton');
      const releaseNote = page.querySelector('#ReleaseNote');
      const clearSig = page.querySelector('#ClearSignature');
      const hint = page.querySelector('.ins-sign-hint');
      if (signButton) signButton.disabled = released || signing || !hasSignature;
      if (clearSig) clearSig.disabled = released || !hasSignature;
      if (hint) hint.classList.toggle('hidden', hasSignature || released);
      if (releaseNote) {
        releaseNote.textContent = released
          ? `Signed by ${Technician} · ${ctx.data.hero?.statusNote || 'Released to service · 03:12'}`
          : 'Releases CA 2207 for the 05:52 departure';
        releaseNote.classList.toggle('released', released);
      }
    }

    function saveInk() {
      if (!scopeInk) return;
      localStorage.setItem(StorageKey, scopeInk.save());
      ctx.showInfo?.('Annotations saved', 'Borescope ink was saved for this workstation.');
    }

    function restoreInk() {
      const saved = localStorage.getItem(StorageKey);
      if (!saved) {
        ctx.showInfo?.('Nothing to restore', 'No saved borescope annotations were found.');
        return;
      }
      scopeInk?.restore(saved);
      ctx.showInfo?.('Annotations restored', 'Saved borescope ink has been restored.');
    }

    function signAndRelease() {
      if (released || signing || !signatureInk || signatureInk.isEmpty) return;
      signing = true;
      updateReleaseState();
      ctx.data.signOff(Technician, Finding);
      released = true;
      ctx.showInfo?.('Released to service', `${window.Hangar?.HeroFlight || 'CA 2207'} is airworthy for the 05:52 departure.`);
      build();
      signing = false;
    }

    function resetPage() {
      scopeInk?.clear();
      signatureInk?.clear();
      released = ctx.data.hero?.status === 'Healthy';
      drawTool = 'pen';
      color = Colors[0].value;
      rulerOn = false;
      colorOpen = false;
      build();
    }

    build();
    unsubs.push(ctx.data.on('reset', resetPage));
    unsubs.push(ctx.data.on('heroStatusChanged', () => { released = ctx.data.hero?.status === 'Healthy'; build(); }));
    if (ctx.theme?.onChanged) unsubs.push(ctx.theme.onChanged(() => {
      signatureInk?.setColor(UI.css('--text-primary') || '#F2F6FA');
    }));

    return {
      unmount() {
        for (const unsub of unsubs) unsub();
        scopeInk?.destroy();
        signatureInk?.destroy();
        root.innerHTML = '';
      },
    };
  }

  window.Pages = window.Pages || {};
  window.Pages.inspections = { fullBleed: true, mount };
})();
