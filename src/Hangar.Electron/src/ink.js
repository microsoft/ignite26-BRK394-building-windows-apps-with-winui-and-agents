'use strict';

(function () {
  const TAU = Math.PI * 2;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

  function rgba(hex, alpha) {
    const h = String(hex || '#FFD400').trim();
    const m = h.match(/^#?([0-9a-f]{3}|[0-9a-f]{6})$/i);
    let r = 255, g = 212, b = 0;
    if (m) {
      let x = m[1];
      if (x.length === 3) x = x.replace(/./g, '$&$&');
      const n = parseInt(x, 16);
      r = (n >> 16) & 255; g = (n >> 8) & 255; b = n & 255;
    }
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }

  class InkSurface {
    constructor(canvas, opts = {}) {
      if (!(canvas instanceof HTMLCanvasElement)) throw new TypeError('InkSurface requires a canvas');
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d', { alpha: true });
      this.opts = opts;
      this.strokes = [];
      this.active = null;
      this.pointerId = null;
      this.erasing = false;
      this.listeners = new Map([['changed', new Set()]]);
      this.tool = 'pen';
      this.drawTool = 'pen';
      this.color = opts.color || '#FFD400';
      this.penSize = opts.size || 3.2;
      this.highlighterSize = opts.highlighterSize || 18;
      this.eraserRadius = opts.eraserRadius || 16;
      this.w = 1;
      this.h = 1;
      this.dpr = 1;
      this.dirty = true;
      this.frame = 0;
      this.ruler = {
        visible: !!opts.ruler,
        cx: 0.52,
        cy: 0.77,
        angle: opts.rulerAngle || 0,
        length: opts.rulerLength || 0.74,
        drag: null,
      };
      this.backing = document.createElement('canvas');
      this.backCtx = this.backing.getContext('2d', { alpha: true });

      Object.assign(this.canvas.style, {
        touchAction: 'none',
        userSelect: 'none',
        display: this.canvas.style.display || 'block',
      });
      this.canvas.tabIndex = this.canvas.tabIndex >= 0 ? this.canvas.tabIndex : 0;

      this._handlers = [
        ['pointerdown', e => this._down(e)],
        ['pointermove', e => this._move(e)],
        ['pointerup', e => this._up(e)],
        ['pointercancel', e => this._cancel(e)],
        ['pointerleave', e => { if (this.pointerId == null) this._cursor(null); }],
      ];
      for (const [name, fn] of this._handlers) this.canvas.addEventListener(name, fn);
      this.ro = new ResizeObserver(() => this._resize());
      this.ro.observe(this.canvas);
      if (this.canvas.parentElement) this.ro.observe(this.canvas.parentElement);
      this._resize();
    }

    get isEmpty() { return this.strokes.length === 0 && !this.active; }

    on(name, fn) {
      if (!this.listeners.has(name)) this.listeners.set(name, new Set());
      this.listeners.get(name).add(fn);
      return () => this.listeners.get(name)?.delete(fn);
    }

    setTool(name) {
      if (name === 'ruler') {
        this.ruler.visible = !this.ruler.visible;
        this.tool = 'ruler';
        this._draw();
        return;
      }
      if (!['pen', 'highlighter', 'eraser'].includes(name)) return;
      this.tool = name;
      if (name !== 'eraser') this.drawTool = name;
      this._cursor(null);
      this._draw();
    }

    setRulerVisible(visible) {
      this.ruler.visible = !!visible;
      this._draw();
    }

    setColor(hex) {
      this.color = hex || this.color;
    }

    clear() {
      const had = this.strokes.length || this.active;
      this.strokes = [];
      this.active = null;
      this.pointerId = null;
      this.dirty = true;
      this._draw();
      if (had) this._emit('changed');
    }

    undo() {
      if (!this.strokes.length) return;
      this.strokes.pop();
      this.dirty = true;
      this._draw();
      this._emit('changed');
    }

    save() {
      return JSON.stringify({
        version: 1,
        strokes: this.strokes.map(s => ({
          tool: s.tool,
          color: s.color,
          size: s.size,
          points: s.points.map(p => [p.x, p.y, p.p]),
        })),
      });
    }

    restore(json) {
      if (!json) return;
      let data;
      try { data = typeof json === 'string' ? JSON.parse(json) : json; }
      catch { return; }
      const list = Array.isArray(data) ? data : data.strokes;
      if (!Array.isArray(list)) return;
      this.strokes = list.map(s => ({
        tool: s.tool === 'highlighter' ? 'highlighter' : 'pen',
        color: s.color || this.color,
        size: Number(s.size) || (s.tool === 'highlighter' ? this.highlighterSize : this.penSize),
        points: (s.points || []).map(p => Array.isArray(p)
          ? { x: clamp(+p[0] || 0, 0, 1), y: clamp(+p[1] || 0, 0, 1), p: clamp(+p[2] || 0.55, 0.1, 1) }
          : { x: clamp(+p.x || 0, 0, 1), y: clamp(+p.y || 0, 0, 1), p: clamp(+p.p || 0.55, 0.1, 1) })
          .filter(p => Number.isFinite(p.x) && Number.isFinite(p.y)),
      })).filter(s => s.points.length);
      this.dirty = true;
      this._draw();
      this._emit('changed');
    }

    destroy() {
      for (const [name, fn] of this._handlers) this.canvas.removeEventListener(name, fn);
      this.ro?.disconnect();
      if (this.frame) cancelAnimationFrame(this.frame);
      this.listeners.clear();
    }

    _emit(name) {
      for (const fn of [...(this.listeners.get(name) || [])]) {
        try { fn(this); } catch (e) { setTimeout(() => { throw e; }); }
      }
    }

    _resize() {
      const r = this.canvas.getBoundingClientRect();
      const w = Math.max(1, r.width || this.canvas.clientWidth || 1);
      const h = Math.max(1, r.height || this.canvas.clientHeight || 1);
      const dpr = Math.max(1, window.devicePixelRatio || 1);
      const pw = Math.round(w * dpr), ph = Math.round(h * dpr);
      if (this.canvas.width !== pw || this.canvas.height !== ph) {
        this.canvas.width = pw; this.canvas.height = ph;
        this.backing.width = pw; this.backing.height = ph;
        this.w = w; this.h = h; this.dpr = dpr;
        this.dirty = true;
      } else {
        this.w = w; this.h = h; this.dpr = dpr;
      }
      this._draw();
    }

    _point(e) {
      const r = this.canvas.getBoundingClientRect();
      let x = e.clientX - r.left;
      let y = e.clientY - r.top;
      ({ x, y } = this._snapToRuler(x, y));
      const pressure = e.pressure && e.pressure > 0 ? e.pressure : (e.pointerType === 'mouse' ? 0.55 : 0.65);
      return { x: clamp(x / this.w, 0, 1), y: clamp(y / this.h, 0, 1), p: clamp(pressure, 0.12, 1) };
    }

    _rawPoint(e) {
      const r = this.canvas.getBoundingClientRect();
      return { x: e.clientX - r.left, y: e.clientY - r.top };
    }

    _events(e) {
      const list = typeof e.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : null;
      return list && list.length ? list : [e];
    }

    _down(e) {
      if (this.pointerId != null) return;
      this.canvas.setPointerCapture?.(e.pointerId);
      this.pointerId = e.pointerId;
      const raw = this._rawPoint(e);
      const hitRuler = this.ruler.visible && this._hitRuler(raw);
      if (this.tool === 'ruler' || hitRuler) {
        this.ruler.drag = this._beginRulerDrag(raw);
        e.preventDefault();
        this._draw();
        return;
      }
      this.erasing = this.tool === 'eraser' || !!(e.buttons & 32);
      if (this.erasing) {
        this._erase(raw);
      } else {
        const tool = this.tool === 'highlighter' ? 'highlighter' : this.drawTool;
        this.active = {
          tool,
          color: tool === 'highlighter' ? this.color : this.color,
          size: tool === 'highlighter' ? this.highlighterSize : this.penSize,
          points: [this._point(e)],
        };
      }
      e.preventDefault();
      this._draw();
    }

    _move(e) {
      const raw = this._rawPoint(e);
      this._cursor(raw);
      if (this.pointerId !== e.pointerId) return;
      if (this.ruler.drag) {
        this._dragRuler(raw);
        e.preventDefault();
        this._draw();
        return;
      }
      if (this.erasing) {
        for (const ev of this._events(e)) this._erase(this._rawPoint(ev));
      } else if (this.active) {
        for (const ev of this._events(e)) {
          const p = this._point(ev);
          const last = this.active.points[this.active.points.length - 1];
          if (!last || Math.hypot((p.x - last.x) * this.w, (p.y - last.y) * this.h) >= 0.7) {
            this.active.points.push(p);
          }
        }
      }
      e.preventDefault();
      this._draw();
    }

    _up(e) {
      if (this.pointerId !== e.pointerId) return;
      this.canvas.releasePointerCapture?.(e.pointerId);
      if (this.ruler.drag) {
        this.ruler.drag = null;
      } else if (this.active) {
        if (this.active.points.length) this.strokes.push(this.active);
        this.active = null;
        this.dirty = true;
        this._emit('changed');
      }
      this.pointerId = null;
      this.erasing = false;
      e.preventDefault();
      this._draw();
    }

    _cancel(e) {
      if (e && this.pointerId !== e.pointerId) return;
      this.active = null;
      this.pointerId = null;
      this.erasing = false;
      this.ruler.drag = null;
      this._draw();
    }

    _erase(p) {
      const before = this.strokes.length;
      this.strokes = this.strokes.filter(s => !this._hitStroke(s, p, this.eraserRadius));
      if (this.strokes.length !== before) {
        this.dirty = true;
        this._emit('changed');
      }
    }

    _hitStroke(stroke, p, radius) {
      const pts = stroke.points.map(q => ({ x: q.x * this.w, y: q.y * this.h }));
      const r = radius + (stroke.size || 3) * 0.5;
      if (pts.length === 1) return Math.hypot(pts[0].x - p.x, pts[0].y - p.y) <= r;
      for (let i = 1; i < pts.length; i++) {
        const a = pts[i - 1], b = pts[i];
        const abx = b.x - a.x, aby = b.y - a.y;
        const den = abx * abx + aby * aby || 1;
        const t = clamp(((p.x - a.x) * abx + (p.y - a.y) * aby) / den, 0, 1);
        if (Math.hypot(a.x + abx * t - p.x, a.y + aby * t - p.y) <= r) return true;
      }
      return false;
    }

    _lineInfo() {
      const length = Math.min(this.w, this.h) * this.ruler.length;
      const cx = this.ruler.cx * this.w, cy = this.ruler.cy * this.h;
      const c = Math.cos(this.ruler.angle), s = Math.sin(this.ruler.angle);
      return { length, cx, cy, c, s };
    }

    _toRulerLocal(p) {
      const l = this._lineInfo();
      const dx = p.x - l.cx, dy = p.y - l.cy;
      return { x: dx * l.c + dy * l.s, y: -dx * l.s + dy * l.c, info: l };
    }

    _hitRuler(p) {
      const q = this._toRulerLocal(p);
      return Math.abs(q.x) <= q.info.length / 2 + 24 && Math.abs(q.y) <= 34;
    }

    _beginRulerDrag(p) {
      const q = this._toRulerLocal(p);
      const rotate = Math.abs(q.x) > q.info.length / 2 - 60 || Math.abs(q.y) > 22;
      return {
        mode: rotate ? 'rotate' : 'move',
        ox: p.x - this.ruler.cx * this.w,
        oy: p.y - this.ruler.cy * this.h,
      };
    }

    _dragRuler(p) {
      if (this.ruler.drag.mode === 'move') {
        this.ruler.cx = clamp((p.x - this.ruler.drag.ox) / this.w, 0.1, 0.9);
        this.ruler.cy = clamp((p.y - this.ruler.drag.oy) / this.h, 0.1, 0.9);
      } else {
        this.ruler.angle = Math.atan2(p.y - this.ruler.cy * this.h, p.x - this.ruler.cx * this.w);
      }
    }

    _snapToRuler(x, y) {
      if (!this.ruler.visible || this.tool === 'ruler') return { x, y };
      const q = this._toRulerLocal({ x, y });
      if (Math.abs(q.x) <= q.info.length / 2 + 8 && Math.abs(q.y) <= 18) {
        return {
          x: q.info.cx + q.x * q.info.c,
          y: q.info.cy + q.x * q.info.s,
        };
      }
      return { x, y };
    }

    _cursor(p) {
      if (this.tool === 'eraser') this.canvas.style.cursor = 'cell';
      else if (this.tool === 'ruler' || (p && this.ruler.visible && this._hitRuler(p))) this.canvas.style.cursor = 'grab';
      else this.canvas.style.cursor = 'crosshair';
    }

    _draw() {
      if (this.frame) return;
      this.frame = requestAnimationFrame(() => {
        this.frame = 0;
        if (this.dirty) this._rebuild();
        const ctx = this.ctx;
        ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
        ctx.clearRect(0, 0, this.w, this.h);
        ctx.drawImage(this.backing, 0, 0, this.w, this.h);
        if (this.active) this._drawStroke(ctx, this.active);
        if (this.ruler.visible) this._drawRuler(ctx);
      });
    }

    _rebuild() {
      this.backCtx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      this.backCtx.clearRect(0, 0, this.w, this.h);
      for (const s of this.strokes) this._drawStroke(this.backCtx, s);
      this.dirty = false;
    }

    _drawStroke(ctx, stroke) {
      const pts = stroke.points;
      if (!pts.length) return;
      ctx.save();
      ctx.globalCompositeOperation = 'source-over';
      ctx.lineJoin = 'round';
      ctx.lineCap = stroke.tool === 'highlighter' ? 'butt' : 'round';
      ctx.strokeStyle = stroke.tool === 'highlighter' ? rgba(stroke.color, 0.38) : stroke.color;
      ctx.fillStyle = ctx.strokeStyle;
      if (pts.length === 1) {
        const p = pts[0], width = this._width(stroke, p.p);
        ctx.beginPath();
        ctx.arc(p.x * this.w, p.y * this.h, width / 2, 0, TAU);
        ctx.fill();
        ctx.restore();
        return;
      }
      const px = i => ({ x: pts[i].x * this.w, y: pts[i].y * this.h, p: pts[i].p });
      let a = px(0);
      for (let i = 1; i < pts.length; i++) {
        const b = px(i);
        const next = i < pts.length - 1 ? px(i + 1) : b;
        const mid = { x: (b.x + next.x) / 2, y: (b.y + next.y) / 2, p: (b.p + next.p) / 2 };
        ctx.lineWidth = this._width(stroke, (a.p + b.p) / 2);
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        if (i < pts.length - 1) ctx.quadraticCurveTo(b.x, b.y, mid.x, mid.y);
        else ctx.lineTo(b.x, b.y);
        ctx.stroke();
        a = mid;
      }
      ctx.restore();
    }

    _width(stroke, pressure) {
      if (stroke.tool === 'highlighter') return stroke.size;
      return stroke.size * (0.65 + clamp(pressure, 0.12, 1) * 0.7);
    }

    _drawRuler(ctx) {
      const { length, cx, cy, c, s } = this._lineInfo();
      const h = 42;
      ctx.save();
      ctx.translate(cx, cy);
      ctx.rotate(this.ruler.angle);
      ctx.fillStyle = 'rgba(58, 209, 240, .16)';
      ctx.strokeStyle = 'rgba(58, 209, 240, .72)';
      ctx.lineWidth = 1;
      this._roundRect(ctx, -length / 2, -h / 2, length, h, 10);
      ctx.fill();
      ctx.stroke();
      ctx.strokeStyle = 'rgba(242, 246, 250, .62)';
      for (let x = -length / 2 + 16, i = 0; x < length / 2 - 8; x += 18, i++) {
        const tall = i % 5 === 0;
        ctx.beginPath();
        ctx.moveTo(x, h / 2 - 4);
        ctx.lineTo(x, h / 2 - (tall ? 18 : 11));
        ctx.stroke();
      }
      ctx.strokeStyle = 'rgba(58, 209, 240, .95)';
      ctx.beginPath();
      ctx.moveTo(-length / 2 + 12, 0);
      ctx.lineTo(length / 2 - 12, 0);
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(-length / 2 + 16, 0, 4, 0, TAU);
      ctx.arc(length / 2 - 16, 0, 4, 0, TAU);
      ctx.fillStyle = 'rgba(58, 209, 240, .9)';
      ctx.fill();
      ctx.restore();

      // Touch a read of c/s so lint/check catches stale line-info mistakes during development.
      void c; void s;
    }

    _roundRect(ctx, x, y, w, h, r) {
      const rr = Math.min(r, w / 2, h / 2);
      ctx.beginPath();
      ctx.moveTo(x + rr, y);
      ctx.arcTo(x + w, y, x + w, y + h, rr);
      ctx.arcTo(x + w, y + h, x, y + h, rr);
      ctx.arcTo(x, y + h, x, y, rr);
      ctx.arcTo(x, y, x + w, y, rr);
      ctx.closePath();
    }
  }

  window.InkSurface = InkSurface;
})();
