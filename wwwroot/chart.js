const colors = ['#6be1bb', '#7cb5fc', '#ceb0fb'];
export class Chart {
  constructor(canvas, { spark = false } = {}) {
    this.canvas = canvas; this.spark = spark; this.series = []; this.bounds = [0, 1];
    this.tooltip = canvas.parentElement.querySelector('.chart-tooltip');
    this.resize = new ResizeObserver(() => this.draw()); this.resize.observe(canvas);
    if (!spark) {
      canvas.addEventListener('pointermove', event => {
        const rect = canvas.getBoundingClientRect();
        const rotated = document.body.classList.contains('focus') && window.matchMedia('(orientation: portrait)').matches;
        this.pointer = rotated ? event.clientY - rect.top : event.clientX - rect.left; this.draw();
      });
      canvas.addEventListener('pointerleave', () => { this.pointer = null; if (this.tooltip) this.tooltip.hidden = true; this.draw(); });
    }
  }
  update(series, bounds, unit = '', elapsed = false) {
    this.series = series; this.bounds = bounds; this.unit = unit; this.elapsed = elapsed; this.draw();
  }
  draw() {
    // Draw in local dimensions even when the focus surface is rotated.
    const c = this.canvas, w = c.clientWidth, h = c.clientHeight;
    if (w < 1 || h < 1) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    const ctx = c.getContext('2d'); ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
    const left = this.spark ? 0 : 39, right = this.spark ? 0 : 12, top = this.spark ? 4 : 15, bottom = this.spark ? 2 : 29;
    const pw = w - left - right, ph = h - top - bottom;
    const [minX, maxX] = this.bounds;
    let lo = Infinity, hi = -Infinity;
    for (const s of this.series) for (const p of s.points) if (p.x >= minX && p.x <= maxX && p.y != null) { lo = Math.min(lo, p.y); hi = Math.max(hi, p.y); }
    const hasData = Number.isFinite(lo);
    if (!hasData) { lo = 0; hi = this.unit === '°C' ? 100 : 3000; }
    else if (this.unit === '°C' && !this.spark) { lo = Math.max(0, Math.floor((lo - 8) / 10) * 10); hi = Math.ceil((hi + 8) / 10) * 10; }
    else { const pad = Math.max((hi - lo) * .2, this.unit === 'RPM' ? 100 : 3); lo = Math.max(0, lo - pad); hi += pad; }
    if (hi <= lo) hi = lo + 1;
    const x = value => left + (value - minX) / Math.max(1, maxX - minX) * pw;
    const y = value => top + (hi - value) / (hi - lo) * ph;
    if (!this.spark) {
      ctx.font = '10px -apple-system,Segoe UI,sans-serif';
      for (let i = 0; i <= 4; i++) {
        const yy = top + ph * i / 4;
        ctx.strokeStyle = '#ffffff08'; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(left, yy); ctx.lineTo(w - right, yy); ctx.stroke();
        ctx.fillStyle = '#587185'; ctx.textAlign = 'right'; ctx.fillText(Math.round(hi - (hi - lo) * i / 4).toLocaleString(), left - 9, yy + 3);
      }
      for (let i = 0; i <= 4; i++) {
        const t = minX + (maxX - minX) * i / 4;
        const text = this.elapsed ? `${Math.round(t / 60)} min` : new Date(t).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
        ctx.textAlign = i === 0 ? 'left' : i === 4 ? 'right' : 'center'; ctx.fillText(text, left + pw * i / 4, h - 8);
      }
    }
    ctx.save(); ctx.beginPath(); ctx.rect(left, 0, pw, h - bottom + 1); ctx.clip();
    this.series.forEach((s, index) => {
      const color = s.color || colors[index % colors.length];
      ctx.lineWidth = this.spark ? 1.5 : 1.8; ctx.strokeStyle = color; ctx.lineJoin = 'round';
      ctx.setLineDash(s.dashed ? [5, 4] : []);
      ctx.beginPath(); let previous = null;
      for (const p of s.points) {
        if (p.x < minX || p.x > maxX || p.y == null) { previous = null; continue; }
        // A missing poll is a gap, not an interpolated temperature.
        const maxGap = this.elapsed ? 5 : 5000;
        if (!previous || p.x - previous.x > maxGap) ctx.moveTo(x(p.x), y(p.y));
        else ctx.lineTo(x(p.x), y(p.y));
        previous = p;
      }
      ctx.stroke(); ctx.setLineDash([]);
      const last = s.points.filter(p => p.x >= minX && p.x <= maxX && p.y != null).at(-1);
      if (last) { ctx.fillStyle = color; ctx.beginPath(); ctx.arc(x(last.x), y(last.y), this.spark ? 2 : 3, 0, Math.PI * 2); ctx.fill(); }
    });
    ctx.restore();
    if (!hasData && !this.spark) { ctx.fillStyle = '#627d91'; ctx.textAlign = 'center'; ctx.font = '12px -apple-system,Segoe UI,sans-serif'; ctx.fillText('Waiting for recorded readings', left + pw / 2, top + ph / 2); }
    if (this.pointer != null && this.tooltip && !this.spark) {
      const px = Math.max(left, Math.min(w - right, this.pointer)), t = minX + (px - left) / pw * (maxX - minX);
      ctx.strokeStyle = '#a4bbce44'; ctx.beginPath(); ctx.moveTo(px, top); ctx.lineTo(px, h - bottom); ctx.stroke();
      const label = this.elapsed ? `${(t / 60).toFixed(1)} min` : new Date(t).toLocaleTimeString();
      const lines = [label];
      for (const s of this.series) {
        const nearest = s.points.reduce((best, p) => !best || Math.abs(p.x - t) < Math.abs(best.x - t) ? p : best, null);
        const valid = nearest && Math.abs(nearest.x - t) < (this.elapsed ? 5 : 5000) && nearest.y != null;
        lines.push(`${s.name}: ${valid ? nearest.y.toFixed(this.unit === 'RPM' ? 0 : 1) + ' ' + this.unit : '—'}`);
      }
      this.tooltip.textContent = lines.join('\n'); this.tooltip.hidden = false;
      this.tooltip.style.left = `${Math.max(0, Math.min(w - this.tooltip.offsetWidth, px + 12))}px`; this.tooltip.style.top = '20px';
    }
  }
}
