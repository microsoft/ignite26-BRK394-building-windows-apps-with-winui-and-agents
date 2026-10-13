'use strict';

(function () {
  const ROW_H = 41;
  const OVERSCAN = 6;
  const SEARCH_DEBOUNCE = 120;
  const HERO_TAIL = () => window.Hangar?.HeroTail || 'N2207C';
  const STATUS_ORDER = { Open: 0, InProgress: 1, Deferred: 2, Closed: 3 };
  const STATUS_CLASS = { Open: 'accent', InProgress: 'attention', Deferred: 'critical', Closed: 'healthy' };
  const STATUS_TEXT = { Open: 'Open', InProgress: 'In progress', Deferred: 'Deferred', Closed: 'Closed' };
  const SORTABLE_COLUMNS = [
    ['number', 'WO #'], ['tail', 'Tail'], ['component', 'Component'], ['ata', 'ATA'], ['finding', 'Finding'],
    ['technician', 'Technician'], ['status', 'Status'], ['partNumber', 'Part #'], ['opened', 'Opened'],
    ['hours', 'Hours'], ['signOff', 'Sign-off'],
  ];

  const addMonths = (time, months) => {
    const d = new Date(time);
    const month = d.getUTCMonth() + months;
    const year = d.getUTCFullYear() + Math.floor(month / 12);
    const normalizedMonth = ((month % 12) + 12) % 12;
    const daysInMonth = new Date(Date.UTC(year, normalizedMonth + 1, 0)).getUTCDate();
    return Date.UTC(year, normalizedMonth, Math.min(d.getUTCDate(), daysInMonth), d.getUTCHours(), d.getUTCMinutes());
  };

  const fmtInt = n => (window.UI?.fmtInt || (v => v.toLocaleString('en-US')))(Math.round(n));
  const statusText = wo => wo.statusText || STATUS_TEXT[wo.status] || wo.status || '';
  const isOpenStatus = wo => wo.status !== 'Closed';
  const workOrderKey = wo => `w:${wo.number}`;

  function ensureStylesheet() {
    if (document.querySelector('link[data-page-css="workorders"]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'pages/workorders.css';
    link.dataset.pageCss = 'workorders';
    document.head.appendChild(link);
  }

  function normalizeParam(param) {
    const normalized = { query: '', tail: '', component: '', part: '' };
    if (typeof param === 'string') normalized.query = param;
    else if (param && typeof param === 'object') {
      normalized.query = param.query || '';
      normalized.tail = param.tail || '';
      normalized.component = param.component || '';
      normalized.part = param.part || '';
    }
    return normalized;
  }

  function matchesQuery(wo, query) {
    if (!query) return true;
    const q = query.toLocaleLowerCase();
    return [wo.tail, wo.component, wo.numberText, wo.finding, wo.partNumber, wo.technician, wo.ata]
      .some(v => String(v || '').toLocaleLowerCase().includes(q));
  }

  function compareValues(a, b) {
    if (typeof a === 'number' && typeof b === 'number') return a - b;
    return String(a ?? '').localeCompare(String(b ?? ''), undefined, { numeric: true, sensitivity: 'base' });
  }

  function sortValue(wo, key) {
    switch (key) {
      case 'number': return wo.number;
      case 'tail': return wo.tail;
      case 'component': return wo.component;
      case 'ata': return wo.ata;
      case 'finding': return wo.finding;
      case 'technician': return wo.technician;
      case 'status': return STATUS_ORDER[wo.status] ?? 99;
      case 'partNumber': return wo.partNumber;
      case 'opened': return wo.opened;
      case 'hours': return wo.hours;
      case 'signOff': return wo.signedBy || '';
      default: return wo.number;
    }
  }

  window.Pages = window.Pages || {};
  window.Pages.workorders = {
    fullBleed: false,
    mount(root, ctx = {}) {
      ensureStylesheet();
      const UI = window.UI;
      const h = UI.h;
      const icon = UI.icon;
      const data = ctx.data || window.Hangar?.data;
      const param = normalizeParam(ctx.param);
      const filters = {
        fan: param.component === 'Fan blade',
        hero: param.tail === HERO_TAIL(),
        recent: false,
        Open: false,
        InProgress: false,
        Deferred: false,
        Closed: false,
      };
      let query = param.query;
      let typedQuery = query;
      let groupByComponent = true;
      let collapsedGroups = new Set();
      let selectedNumber = 0;
      let sort = null;
      let flatRows = [];
      let visibleWorkOrders = [];
      let groupNames = [];
      let raf = 0;
      let debounceTimer = 0;
      let isUnmounted = false;
      let editingNumber = 0;
      let focusedIndex = -1;
      const activeNodes = new Map();
      const pool = [];

      root.classList.add('wo-page');
      root.replaceChildren();

      const page = h('div.wo-shell');
      const header = h('div.wo-header');
      const toolbar = h('div.wo-toolbar');
      const tableCard = h('section.card.wo-table-card', { id: 'WorkOrdersTable', 'data-automation-id': 'WorkOrdersTable', tabindex: '0', role: 'grid', 'aria-label': 'Work orders' });
      const detail = h('section.card.wo-detail', { id: 'DetailStrip', 'data-automation-id': 'DetailStrip' });
      page.append(header, toolbar, tableCard, detail);
      root.append(page);

      const statShowing = h('div.kpi-value.wo-kpi-number', { id: 'WorkOrdersShowing', text: '0' });
      const statOpen = h('div.kpi-value.wo-kpi-number.wo-kpi-accent', { id: 'WorkOrdersOpen', text: '0' });
      const statHours = h('div.kpi-value.wo-kpi-number', { id: 'WorkOrdersHours', text: '0' });
      const filterInput = h('input.text-box.wo-filter-input', {
        id: 'WorkOrderFilter',
        'data-automation-id': 'WorkOrderFilter',
        type: 'text',
        placeholder: 'Filter tail, component, WO#, finding, part...',
        value: typedQuery,
        'aria-label': 'Filter work orders',
      });
      const groupToggle = h('button.chip.wo-group-toggle.on', { id: 'GroupToggle', 'data-automation-id': 'GroupToggle', type: 'button', 'aria-pressed': 'true' }, icon('F168'), h('span', { text: 'Group by component' }));
      const collapseButton = h('button.btn.icon-only.wo-round-button', { id: 'CollapseAllGroups', 'data-automation-id': 'CollapseAllGroups', type: 'button', tip: 'Collapse all groups', 'aria-label': 'Collapse all groups' }, icon('E70E'));
      const expandButton = h('button.btn.icon-only.wo-round-button', { id: 'ExpandAllGroups', 'data-automation-id': 'ExpandAllGroups', type: 'button', tip: 'Expand all groups', 'aria-label': 'Expand all groups' }, icon('E70D'));

      const headerClip = h('div.wo-table-header-clip');
      const headerRow = h('div.wo-grid.wo-table-header-row', { role: 'row' });
      const viewport = h('div.wo-table-viewport', { tabindex: '0' });
      const spacer = h('div.wo-table-spacer');
      const rowsLayer = h('div.wo-rows-layer');
      spacer.append(rowsLayer);
      viewport.append(spacer);
      headerClip.append(headerRow);
      tableCard.append(headerClip, viewport);

      function kpi(labelEl, label) {
        return h('div.wo-kpi', labelEl, h('div.eyebrow.wo-kpi-label', { text: label }));
      }

      function buildHeader() {
        header.replaceChildren(
          h('div.wo-title-block',
            h('div.eyebrow', { id: 'WorkOrdersEyebrow', text: `MAINTENANCE RECORDS · ${fmtInt((data?.workOrders || []).length)} WORK ORDERS` }),
            h('h1.hero', { text: 'Paper trail' }),
            h('p.wo-subtitle.secondary', { text: 'Every finding, every fix, every signature. Group, filter and sort the whole fleet history in one view.' }),
          ),
          h('div.wo-kpis',
            kpi(statShowing, 'SHOWING'),
            kpi(statOpen, 'OPEN'),
            kpi(statHours, 'LABOR HOURS'),
          ),
        );
      }

      function chip(id, text, iconGlyph, isOn, onClick, mono) {
        return h('button.chip.wo-filter-chip', {
          id,
          'data-automation-id': id,
          type: 'button',
          class: isOn ? 'on' : '',
          'aria-pressed': isOn ? 'true' : 'false',
          onClick,
        }, iconGlyph ? icon(iconGlyph) : null, h('span', { class: mono ? 'mono' : '', text }));
      }

      function renderToolbar() {
        toolbar.replaceChildren(
          h('div.wo-filter-box', filterInput, icon('E721', 'wo-filter-icon')),
          h('div.wo-chips',
            chip('ChipFanBlade', 'Fan blade', 'E945', filters.fan, () => toggleFilter('fan')),
            chip('ChipHeroTail', HERO_TAIL(), null, filters.hero, () => toggleFilter('hero'), true),
            chip('ChipLast12Months', 'Last 12 months', null, filters.recent, () => toggleFilter('recent')),
            h('span.wo-divider', { 'aria-hidden': 'true' }),
            chip('ChipOpen', 'Open', null, filters.Open, () => toggleFilter('Open')),
            chip('ChipInProgress', 'In progress', null, filters.InProgress, () => toggleFilter('InProgress')),
            chip('ChipDeferred', 'Deferred', null, filters.Deferred, () => toggleFilter('Deferred')),
            chip('ChipClosed', 'Closed', null, filters.Closed, () => toggleFilter('Closed')),
          ),
          h('div.wo-toolbar-actions', groupToggle, collapseButton, expandButton),
        );
      }

      function renderTableHeader() {
        headerRow.replaceChildren(...SORTABLE_COLUMNS.map(([key, label]) => {
          const current = sort?.key === key;
          return h('button.wo-th', {
            type: 'button',
            role: 'columnheader',
            'data-sort': key,
            'aria-sort': current ? (sort.asc ? 'ascending' : 'descending') : 'none',
            onClick: () => setSort(key),
          }, h('span', { text: label }), current ? icon(sort.asc ? 'E70E' : 'E70D', 'wo-sort-icon') : null);
        }));
      }

      function toggleFilter(key) {
        filters[key] = !filters[key];
        rebuild();
      }

      function setSort(key) {
        sort = sort?.key === key ? { key, asc: !sort.asc } : { key, asc: true };
        rebuild();
      }

      function statusFiltersActive() {
        return filters.Open || filters.InProgress || filters.Deferred || filters.Closed;
      }

      function matchesNav(wo) {
        return (!param.tail || wo.tail === param.tail)
          && (!param.component || wo.component === param.component)
          && (!param.part || wo.mappedPart === param.part || wo.component === param.part);
      }

      function matchesFilters(wo) {
        const recentCutoff = addMonths(window.Hangar?.Now || Date.now(), -12);
        const statusOk = !statusFiltersActive() || Boolean(filters[wo.status]);
        return matchesNav(wo)
          && (!filters.fan || wo.component === 'Fan blade')
          && (!filters.hero || wo.tail === HERO_TAIL())
          && (!filters.recent || wo.opened >= recentCutoff)
          && statusOk
          && matchesQuery(wo, query);
      }

      function sortedItems(items) {
        if (!sort) return items.slice();
        return items.slice().sort((a, b) => {
          const result = compareValues(sortValue(a, sort.key), sortValue(b, sort.key));
          return sort.asc ? result : -result;
        });
      }

      function buildRows() {
        const all = data?.workOrders || [];
        visibleWorkOrders = all.filter(matchesFilters);
        const rows = [];
        groupNames = [];
        if (groupByComponent) {
          const groups = new Map();
          for (const wo of visibleWorkOrders) {
            if (!groups.has(wo.component)) groups.set(wo.component, []);
            groups.get(wo.component).push(wo);
          }
          for (const [name, items] of groups) {
            groupNames.push(name);
            const totalHours = items.reduce((sum, wo) => sum + wo.hours, 0);
            const open = items.filter(isOpenStatus).length;
            rows.push({ kind: 'group', key: `g:${name}`, name, count: items.length, open, hours: totalHours, collapsed: collapsedGroups.has(name) });
            if (!collapsedGroups.has(name)) rows.push(...sortedItems(items).map(wo => ({ kind: 'wo', key: workOrderKey(wo), wo })));
          }
        } else {
          rows.push(...sortedItems(visibleWorkOrders).map(wo => ({ kind: 'wo', key: workOrderKey(wo), wo })));
        }
        flatRows = rows;
      }

      function updateStats() {
        statShowing.textContent = fmtInt(visibleWorkOrders.length);
        statOpen.textContent = fmtInt(visibleWorkOrders.filter(isOpenStatus).length);
        statHours.textContent = fmtInt(visibleWorkOrders.reduce((sum, wo) => sum + wo.hours, 0));
      }

      function updateControls() {
        filterInput.value = typedQuery;
        groupToggle.classList.toggle('on', groupByComponent);
        groupToggle.setAttribute('aria-pressed', groupByComponent ? 'true' : 'false');
        collapseButton.disabled = !groupByComponent;
        expandButton.disabled = !groupByComponent;
        renderToolbar();
        renderTableHeader();
      }

      function renderDetail() {
        const selected = selectedNumber ? (data?.workOrders || []).find(wo => wo.number === selectedNumber) : null;
        const title = selected ? `${selected.numberText} · ${selected.tail} · ${selected.component} — ${selected.finding}` : 'Select a work order';
        const caption = selected
          ? `${statusText(selected)} · ${selected.technician} · opened ${selected.openedText} · ${selected.hours.toFixed(1)} h · ATA ${selected.ata} · P/N ${selected.partNumber}${selected.signedBy ? ` · signed by ${selected.signedBy}` : ''}`
          : 'Double-click Hours to edit inline. Values must be greater than 0.';
        const button = h('button.btn.accent.wo-detail-button', {
          id: 'ShowOnAircraftButton',
          'data-automation-id': 'ShowOnAircraftButton',
          type: 'button',
          disabled: !(selected && selected.isMappable),
          onClick: () => {
            if (selected && selected.isMappable) ctx.navigate?.('aircraft', { tail: selected.tail, part: selected.mappedPart });
          },
        }, icon('E709'), h('span', { text: 'Show on aircraft' }));
        detail.replaceChildren(
          h('div.wo-detail-icon', selected?.isMappable ? icon('E709') : icon('E8FD')),
          h('div.wo-detail-copy',
            h('div.wo-detail-title', { id: 'DetailTitle', text: title }),
            h('div.caption.wo-detail-caption', { id: 'DetailSubtitle', text: caption }),
          ),
          button,
        );
      }

      function rebuild() {
        if (isUnmounted) return;
        buildRows();
        updateStats();
        updateControls();
        spacer.style.height = `${flatRows.length * ROW_H}px`;
        spacer.style.minWidth = 'var(--wo-table-min-width)';
        tableCard.setAttribute('aria-rowcount', String(flatRows.length));
        if (selectedNumber && !visibleWorkOrders.some(wo => wo.number === selectedNumber)) selectedNumber = 0;
        focusedIndex = selectedNumber ? flatRows.findIndex(row => row.kind === 'wo' && row.wo.number === selectedNumber) : focusedIndex;
        renderDetail();
        scheduleVirtualRender();
      }

      function scheduleVirtualRender() {
        if (raf) return;
        raf = requestAnimationFrame(() => {
          raf = 0;
          renderVisibleRows();
        });
      }

      function acquireRow() {
        const row = pool.pop() || h('div.wo-row');
        rowsLayer.appendChild(row);
        return row;
      }

      function releaseRow(index, node) {
        activeNodes.delete(index);
        node.replaceChildren();
        node.className = 'wo-row';
        node.removeAttribute('data-row-key');
        node.removeAttribute('aria-selected');
        node.remove();
        pool.push(node);
      }

      function renderVisibleRows() {
        const scrollTop = viewport.scrollTop;
        const height = viewport.clientHeight || 1;
        const start = Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN);
        const end = Math.min(flatRows.length - 1, Math.ceil((scrollTop + height) / ROW_H) + OVERSCAN);
        for (const [index, node] of [...activeNodes]) {
          if (index < start || index > end) releaseRow(index, node);
        }
        for (let index = start; index <= end; index++) {
          const row = flatRows[index];
          if (!row) continue;
          let node = activeNodes.get(index);
          if (!node) {
            node = acquireRow();
            activeNodes.set(index, node);
          }
          updateRowNode(node, row, index);
        }
        headerRow.style.transform = `translateX(${-viewport.scrollLeft}px)`;
      }

      function updateRowNode(node, row, index) {
        node.style.transform = `translateY(${index * ROW_H}px)`;
        node.dataset.rowKey = row.key;
        node.setAttribute('aria-rowindex', String(index + 1));
        node.className = `wo-row ${row.kind === 'group' ? 'wo-group-row' : 'wo-data-row'}${index === focusedIndex ? ' focused' : ''}`;
        if (row.kind === 'group') {
          updateGroupNode(node, row);
        } else {
          updateWorkOrderNode(node, row.wo, index);
        }
      }

      function updateGroupNode(node, row) {
        const percent = row.count ? Math.max(4, Math.min(100, row.open / row.count * 100)) : 0;
        node.setAttribute('role', 'row');
        node.setAttribute('aria-expanded', row.collapsed ? 'false' : 'true');
        node.onclick = () => {
          if (collapsedGroups.has(row.name)) collapsedGroups.delete(row.name);
          else collapsedGroups.add(row.name);
          rebuild();
        };
        node.ondblclick = null;
        node.replaceChildren(
          h('button.wo-group-content', { type: 'button', 'data-automation-id': `Group_${row.name.replace(/\s+/g, '_')}` },
            icon(row.collapsed ? 'E76C' : 'E70D', 'wo-group-chevron'),
            h('span.wo-group-name', { text: row.name }),
            h('span.wo-group-count', { text: fmtInt(row.count) }),
            h('span.wo-group-spacer'),
            h('span.wo-group-open', { text: `${fmtInt(row.open)} open` }),
            h('span.wo-group-bar', h('span', { style: { width: `${percent}%` } })),
            h('span.mono.wo-group-hours', { text: `${fmtInt(row.hours)} h` }),
          ),
        );
      }

      function cell(className, content, attrs = {}) {
        return h(`div.wo-cell.${className}`, attrs, content);
      }

      function updateWorkOrderNode(node, wo, index) {
        const selected = wo.number === selectedNumber;
        node.onclick = () => selectWorkOrder(wo, index, false);
        node.ondblclick = null;
        node.setAttribute('role', 'row');
        node.setAttribute('aria-selected', selected ? 'true' : 'false');
        node.classList.toggle('selected', selected);
        const hoursCell = cell('hours.mono', editingNumber === wo.number ? buildHoursEditor(wo) : wo.hours.toFixed(1), {
          'data-automation-id': `Hours_${wo.number}`,
          title: 'Double-click to edit labor hours',
          onDblclick: ev => {
            ev.stopPropagation();
            startHoursEdit(wo);
          },
        });
        node.replaceChildren(
          cell('number.mono', wo.numberText),
          cell('tail.mono', wo.tail),
          cell('component', wo.component),
          cell('ata.mono.tertiary', wo.ata),
          cell('finding', wo.finding, { title: wo.finding }),
          cell('technician', h('div.wo-tech', h('span.avatar.wo-tech-avatar', { text: UI.initials(wo.technician) }), h('span', { text: wo.technician }))),
          cell('status', h(`span.pill-tag.${STATUS_CLASS[wo.status] || 'neutral'}`, { text: statusText(wo) })),
          cell('part.mono.tertiary', wo.partNumber),
          cell('opened.mono', wo.openedText),
          hoursCell,
          cell('signoff', wo.status === 'Closed' ? icon('E73E', 'wo-check') : h('span.tertiary', { text: '—' })),
        );
      }

      function startHoursEdit(wo) {
        editingNumber = wo.number;
        scheduleVirtualRender();
        requestAnimationFrame(() => {
          const input = rowsLayer.querySelector(`input[data-edit-number="${wo.number}"]`);
          if (input) {
            input.focus();
            input.select();
          }
        });
      }

      function buildHoursEditor(wo) {
        const input = h('input.wo-hours-editor', {
          'data-edit-number': String(wo.number),
          id: 'HoursEditor',
          type: 'text',
          value: wo.hours.toFixed(1),
          'aria-label': 'Labor hours',
        });
        const validate = () => {
          const value = Number.parseFloat(input.value);
          const ok = Number.isFinite(value) && value > 0;
          input.classList.toggle('invalid', !ok);
          input.setAttribute('aria-invalid', ok ? 'false' : 'true');
          return ok ? value : null;
        };
        const commit = () => {
          const value = validate();
          if (value == null) return false;
          wo.hours = Math.round(value * 10) / 10;
          editingNumber = 0;
          rebuild();
          return true;
        };
        input.addEventListener('input', validate);
        input.addEventListener('keydown', ev => {
          if (ev.key === 'Enter') {
            ev.preventDefault();
            commit();
          } else if (ev.key === 'Escape') {
            ev.preventDefault();
            editingNumber = 0;
            scheduleVirtualRender();
          }
        });
        input.addEventListener('blur', () => {
          if (editingNumber === wo.number && !commit()) input.focus();
        });
        return input;
      }

      function selectWorkOrder(wo, index, scroll) {
        selectedNumber = wo.number;
        focusedIndex = index;
        renderDetail();
        scheduleVirtualRender();
        if (scroll) scrollIndexIntoView(index);
      }

      function scrollIndexIntoView(index) {
        if (index < 0) return;
        const top = index * ROW_H;
        const bottom = top + ROW_H;
        if (top < viewport.scrollTop) viewport.scrollTop = top;
        else if (bottom > viewport.scrollTop + viewport.clientHeight) viewport.scrollTop = bottom - viewport.clientHeight;
      }

      function moveSelection(delta) {
        if (!flatRows.length) return;
        let index = focusedIndex >= 0 ? focusedIndex : flatRows.findIndex(row => row.kind === 'wo' && row.wo.number === selectedNumber);
        if (index < 0) index = delta > 0 ? -1 : flatRows.length;
        do {
          index += delta;
        } while (index >= 0 && index < flatRows.length && flatRows[index].kind !== 'wo');
        if (index >= 0 && index < flatRows.length) selectWorkOrder(flatRows[index].wo, index, true);
      }

      function onKeyDown(ev) {
        if (ev.target && ['INPUT', 'TEXTAREA'].includes(ev.target.tagName)) return;
        if (ev.key === 'ArrowDown') {
          ev.preventDefault();
          ev.stopPropagation();
          moveSelection(1);
        } else if (ev.key === 'ArrowUp') {
          ev.preventDefault();
          ev.stopPropagation();
          moveSelection(-1);
        } else if (ev.key === 'Enter') {
          ev.preventDefault();
          ev.stopPropagation();
          const index = focusedIndex >= 0 ? focusedIndex : flatRows.findIndex(row => row.kind === 'wo' && row.wo.number === selectedNumber);
          if (flatRows[index]?.kind === 'wo') selectWorkOrder(flatRows[index].wo, index, true);
        }
      }

      filterInput.addEventListener('input', () => {
        typedQuery = filterInput.value;
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
          query = typedQuery.trim();
          rebuild();
        }, SEARCH_DEBOUNCE);
      });
      groupToggle.addEventListener('click', () => {
        groupByComponent = !groupByComponent;
        rebuild();
      });
      collapseButton.addEventListener('click', () => {
        collapsedGroups = new Set(groupNames);
        rebuild();
      });
      expandButton.addEventListener('click', () => {
        collapsedGroups = new Set();
        rebuild();
      });
      viewport.addEventListener('scroll', scheduleVirtualRender, { passive: true });
      tableCard.addEventListener('keydown', onKeyDown);
      viewport.addEventListener('keydown', onKeyDown);

      const unsubscribeReset = data?.on?.('reset', () => {
        query = '';
        typedQuery = '';
        Object.assign(filters, { fan: false, hero: false, recent: false, Open: false, InProgress: false, Deferred: false, Closed: false });
        groupByComponent = true;
        collapsedGroups = new Set();
        selectedNumber = 0;
        sort = null;
        editingNumber = 0;
        focusedIndex = -1;
        rebuild();
      });
      const unsubscribeHero = data?.on?.('heroStatusChanged', rebuild);
      const unsubscribeTheme = (ctx.theme || UI.theme)?.onChanged?.(scheduleVirtualRender);

      buildHeader();
      renderToolbar();
      renderTableHeader();
      rebuild();

      return {
        unmount() {
          isUnmounted = true;
          clearTimeout(debounceTimer);
          if (raf) cancelAnimationFrame(raf);
          unsubscribeReset?.();
          unsubscribeHero?.();
          unsubscribeTheme?.();
          root.classList.remove('wo-page');
          root.replaceChildren();
        },
      };
    },
  };
})();
