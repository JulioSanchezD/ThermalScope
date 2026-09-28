import { Chart } from './chart.js';
const $ = id => document.getElementById(id);
const names = { cpuTemp: 'CPU temperature', gpuTemp: 'GPU temperature', gpuHotspot: 'GPU hotspot', cpuFan: 'CPU cooler fan', gpuFan: 'GPU fan', pumpFan: 'Pump', systemFans: 'System fans', cpuPower: 'CPU power', gpuPower: 'GPU power', cpuLoad: 'CPU utilization', gpuLoad: 'GPU utilization', ramUsed: 'Memory used', fps: 'Frame rate', frameTime: 'Frame time' };
const units = { cpuTemp: '°C', gpuTemp: '°C', gpuHotspot: '°C', cpuFan: 'RPM', gpuFan: 'RPM', pumpFan: 'RPM', systemFans: 'RPM', cpuPower: 'W', gpuPower: 'W', cpuLoad: '%', gpuLoad: '%', ramUsed: 'GB', fps: 'FPS', frameTime: 'ms' };
let frames = [], live = null, range = 300, connected = false, sensorsSignature = '', settings = null, lastSuccess = 0, sessionDetails = [], compareGeneration = 0;
let savedSessions = [], selectedSessions = new Set();
const comparisonColors = ['#6be1bb', '#7cb5fc', '#ceb0fb', '#ffb566', '#ff877d', '#78d9eb', '#e7db77', '#ee9fce'];
for (const [key, label] of Object.entries(names)) {
  const option = document.createElement('option'); option.value = key; option.textContent = `${label} · ${units[key]}`; $('compare-metric').append(option);
}
let serverTime = Date.now(), receivedAt = performance.now();
const serverNow = () => serverTime + performance.now() - receivedAt;
const tempChart = new Chart($('temp-chart')), fanChart = new Chart($('fan-chart')), compareChart = new Chart($('compare-chart'));
const sparks = [...document.querySelectorAll('.spark')].map(c => ({ key: c.dataset.key, chart: new Chart(c, { spark: true }) }));

async function api(path, body) {
  const response = await fetch(path, { cache: 'no-store', signal: AbortSignal.timeout(8000), ...(body !== undefined ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}) });
  if (!response.ok) { const data = await response.json().catch(() => ({})); throw new Error(data.error || `Request failed (${response.status})`); }
  return response.json();
}
let toastTimer;
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('toast').hidden = true; }, 5000); }
const value = (v, decimals = 0) => v == null ? '—' : v.toLocaleString(undefined, { maximumFractionDigits: decimals, minimumFractionDigits: decimals });
const element = (tag, text, cls) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (cls) el.className = cls; return el; };
function addFrames(items) {
  const existing = new Map(frames.map(f => [f.sequence, f]));
  for (const f of items) existing.set(f.sequence, f);
  frames = [...existing.values()].sort((a, b) => a.sequence - b.sequence).slice(-7200);
}
function showView(name) {
  for (const view of ['live', 'sessions', 'compare', 'sensors']) $(`${view}-view`).hidden = view !== name;
  document.querySelectorAll('.tab').forEach(b => b.classList.toggle('active', b.dataset.view === name));
  if (name === 'sessions' || name === 'compare') refreshSessions().catch(e => toast(e.message));
  if (name === 'sensors') refreshSettings().catch(e => toast(e.message));
  requestAnimationFrame(draw);
}
document.querySelectorAll('.tab').forEach(b => b.addEventListener('click', () => showView(b.dataset.view)));
const focusHome = $('focus').parentElement;
$('focus').addEventListener('click', () => {
  showView('live');
  const focused = document.body.classList.toggle('focus');
  (focused ? $('live-view') : focusHome).append($('focus'));
  $('focus').setAttribute('aria-pressed', String(focused));
  $('focus').setAttribute('aria-label', focused ? 'Exit focus mode' : 'Toggle focus mode');
  requestAnimationFrame(draw);
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && document.body.classList.contains('focus')) $('focus').click();
});
$('range').addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; range = Number(b.dataset.seconds); $('range').querySelectorAll('button').forEach(x => x.classList.toggle('selected', x === b)); draw(); });

function draw() {
  const now = serverNow(), bounds = [now - range * 1000, now];
  const series = keys => keys.map(key => ({ name: names[key], points: frames.map(f => ({ x: Date.parse(f.time), y: f.metrics[key] ?? null })) }));
  tempChart.update(series(['cpuTemp', 'gpuTemp', 'gpuHotspot']), bounds, '°C');
  fanChart.update(series(['cpuFan', 'gpuFan', 'pumpFan', 'systemFans']).map((s, i) => i === 3 ? { ...s, color: '#ffb566' } : s), bounds, 'RPM');
  for (const s of sparks) s.chart.update([{ ...series([s.key])[0], color: s.key === 'cpuTemp' ? '#6be1bb' : s.key === 'gpuTemp' ? '#7cb5fc' : '#ceb0fb' }], [now - 60000, now], units[s.key]);
  drawComparison();
}

function renderLive() {
  const f = live?.frame, stale = !connected || !f || serverNow() - Date.parse(f.time) > 6000;
  $('connection').className = `status ${stale ? 'stale' : 'connected'}`;
  $('connection').replaceChildren(element('i'), document.createTextNode(stale ? 'Reconnecting' : 'Live · 1 sec'));
  for (const key of Object.keys(names)) if ($(key)) $(key).textContent = value(stale ? null : f.metrics[key], key.endsWith('Temp') ? 1 : 0);
  $('demo-banner').hidden = !f?.demo;
  const errors = [...(f?.warnings || []), ...(live?.recordingError ? [live.recordingError] : [])];
  if (stale && lastSuccess) errors.unshift('Connection interrupted. Recording continues on the PC while ThermalScope is running.');
  $('errors').hidden = errors.length === 0; $('errors').textContent = errors.join('\n');
  $('last-update').textContent = f ? `Updated ${new Date(f.time).toLocaleTimeString()}.` : 'Discovering sensors…';
  const active = live?.active;
  $('record-state').classList.toggle('recording', !!active);
  $('record-label').textContent = active ? `Recording · ${active.name}` : 'Ready to record';
  if (f?.sensors) {
    const hardware = type => [...new Set(f.sensors.filter(s => type(s.hardwareType)).map(s => s.hardware))].join(' · ');
    const cpu = hardware(t => t === 'Cpu'), gpu = hardware(t => t.startsWith('Gpu'));
    $('hardware-summary').textContent = [cpu, gpu].filter(Boolean).join(' · ') || 'Your PC · live sensor readings';
  }
  const elapsed = active ? Math.max(0, Math.floor((serverNow() - Date.parse(active.started)) / 1000)) : 0;
  $('timer').textContent = `${String(Math.floor(elapsed / 60)).padStart(2, '0')}:${String(elapsed % 60).padStart(2, '0')}`;
  $('start').hidden = !!active; $('stop').hidden = !active; $('stop').disabled = stale;
  $('start').disabled = stale || !f || !Object.values(f.metrics).some(v => v != null);
  $('record-form').querySelectorAll('input,select').forEach(el => { el.disabled = !!active; });
  $('mapping-form').querySelectorAll('select,button').forEach(el => { el.disabled = !!active || stale; });
  if (!$('sensors-view').hidden && f) renderSensors(f.sensors);
}

async function poll() {
  try {
    const next = await api('/api/live');
    if (next.frame && (!connected || (live?.frame?.sequence ?? 0) + 2 < next.frame.sequence || next.frame.sequence < (live?.frame?.sequence ?? 0))) {
      if (next.frame.sequence < (live?.frame?.sequence ?? 0)) { frames = []; sensorsSignature = ''; }
      frames = await api('/api/history');
    }
    const previousActive = live?.active;
    serverTime = Date.parse(next.serverTime || next.frame?.time) || Date.now(); receivedAt = performance.now();
    live = next; connected = true; lastSuccess = Date.now();
    if (next.frame) addFrames([next.frame]);
    if (previousActive && !next.active) { toast(next.recordingError ? 'Recording encountered an error; check the message above.' : 'Session saved. Find it in Sessions.'); if (!$('sessions-view').hidden || !$('compare-view').hidden) await refreshSessions(); }
  } catch { connected = false; }
  renderLive(); draw(); setTimeout(poll, 1000);
}
document.addEventListener('visibilitychange', () => { if (!document.hidden) { connected = false; draw(); } });

$('record-form').addEventListener('submit', async e => {
  e.preventDefault(); $('start').disabled = true;
  try {
    const active = await api('/api/sessions/start', { name: $('session-name').value, cooler: $('cooler').value, game: $('game').value, ambient: $('ambient').value === '' ? null : Number($('ambient').value), notes: $('notes').value, minutes: Number($('duration').value) });
    live = { ...live, active }; renderLive(); toast('Recording started. Your phone can disconnect safely.');
  } catch (error) { toast(error.message); renderLive(); }
});
$('stop').addEventListener('click', async () => {
  $('stop').disabled = true;
  try { await api('/api/sessions/stop', {}); live = { ...live, active: null }; renderLive(); toast('Session saved.'); }
  catch (e) { toast(e.message); renderLive(); }
});

async function refreshSettings() { settings = await api('/api/settings'); sensorsSignature = ''; if (live?.frame) renderSensors(live.frame.sensors); }
function renderSensors(sensors) {
  const signature = sensors.map(s => s.id).join('|');
  if (signature !== sensorsSignature && settings) {
    sensorsSignature = signature; $('mapping-fields').replaceChildren();
    for (const [key, label] of Object.entries(names)) {
      if (key === 'systemFans') continue; // Calculated across fans, not mapped to one sensor.
      const field = element('label', label), select = element('select'); select.dataset.key = key;
      const auto = element('option', `Automatic${settings.resolved[key] ? ' · ' + (sensors.find(s => s.id === settings.resolved[key])?.name || '') : ' · unavailable'}`); auto.value = ''; select.append(auto);
      for (const sensor of sensors.filter(s => s.unit === units[key])) {
        const option = element('option', `${sensor.name} · ${sensor.hardware} [${sensor.source}]`); option.value = sensor.id; select.append(option);
      }
      select.value = settings.mappings[key] || ''; select.disabled = !!live?.active; field.append(select); $('mapping-fields').append(field);
    }
  }
  $('sensor-count').textContent = `${sensors.length} readings`;
  $('sensor-table').replaceChildren();
  for (const sensor of sensors) {
    const row = element('tr'), name = element('td', sensor.name); name.append(element('small', sensor.hardware));
    row.append(name, element('td', `${value(sensor.value, sensor.unit === '°C' ? 1 : 0)} ${sensor.unit}`), element('td', sensor.source)); $('sensor-table').append(row);
  }
}
$('mapping-form').addEventListener('submit', async e => {
  e.preventDefault(); const mappings = {};
  $('mapping-fields').querySelectorAll('select').forEach(s => { mappings[s.dataset.key] = s.value; });
  try { settings = await api('/api/settings', { mappings }); sensorsSignature = ''; if (live?.frame) renderSensors(live.frame.sensors); toast('Sensor assignments saved.'); }
  catch (error) { toast(error.message); }
});

async function refreshSessions() {
  const sessions = await api('/api/sessions'); savedSessions = sessions; $('session-list').replaceChildren();
  if (!sessions.length) $('session-list').append(element('p', 'Your first baseline starts here. Record a session from the live dashboard.', 'empty'));
  for (const s of sessions) {
    const row = element('div', null, 'session-row'), main = element('div', null, 'session-main');
    main.append(element('strong', s.name), element('p', `${s.cooler ? s.cooler + ' · ' : ''}${new Date(s.started).toLocaleString()} · ${s.samples.toLocaleString()} samples · ${s.status}${s.ambient != null ? ' · room ' + s.ambient + ' °C' : ''}`));
    if (s.game || s.notes) main.append(element('p', [s.game, s.notes].filter(Boolean).join(' · ')));
    const download = element('a', '↓ CSV'); download.href = `/api/sessions/${s.id}/csv`; row.append(main, download); $('session-list').append(row);
  }
  selectedSessions = new Set([...selectedSessions].filter(id => sessions.some(s => s.id === id && s.status !== 'recording')));
  renderComparisonPicker();
  await loadComparison();
}
$('refresh-sessions').addEventListener('click', () => refreshSessions().catch(e => toast(e.message)));
function renderComparisonPicker() {
  $('compare-session-list').replaceChildren();
  const query = $('compare-search').value.toLocaleLowerCase().trim();
  const available = savedSessions.filter(s => s.status !== 'recording' && [s.name, s.cooler, s.game, s.notes].join(' ').toLocaleLowerCase().includes(query));
  if (!available.length) $('compare-session-list').append(element('p', savedSessions.length ? 'No matching saved sessions. Active recordings are available after they stop.' : 'Record a session to start comparing.', 'empty'));
  for (const s of available) {
    const label = element('label', null, 'compare-choice'), input = element('input'); input.type = 'checkbox'; input.value = s.id; input.checked = selectedSessions.has(s.id);
    const text = element('span'); text.append(element('strong', s.name), element('small', [s.cooler, s.game, new Date(s.started).toLocaleString(), `${s.samples.toLocaleString()} samples`].filter(Boolean).join(' · ')));
    input.addEventListener('change', () => { input.checked ? selectedSessions.add(s.id) : selectedSessions.delete(s.id); loadComparison(); });
    label.append(input, text); $('compare-session-list').append(label);
  }
  $('compare-selected-count').textContent = `${selectedSessions.size} selected`;
}
$('compare-search').addEventListener('input', renderComparisonPicker);
$('clear-comparison').addEventListener('click', () => { selectedSessions.clear(); renderComparisonPicker(); loadComparison(); });
$('refresh-comparison').addEventListener('click', () => refreshSessions().catch(e => toast(e.message)));
async function loadComparison() {
  const generation = ++compareGeneration;
  $('compare-selected-count').textContent = `${selectedSessions.size} selected`;
  $('compare-loading').textContent = selectedSessions.size ? 'Loading selected sessions…' : 'Select sessions above to overlay their recordings.';
  sessionDetails = []; renderComparisonStats(); drawComparison();
  try {
    // Load sequentially to avoid flooding the local server with large recording requests.
    const details = [];
    for (const id of selectedSessions) {
      details.push(await api(`/api/sessions/${encodeURIComponent(id)}`));
      if (generation !== compareGeneration) return;
    }
    if (generation !== compareGeneration) return;
    sessionDetails = details;
    $('compare-loading').textContent = details.length ? 'Aligned by elapsed time. Compare similar workloads and room conditions.' : 'Select sessions above to overlay their recordings.';
    renderComparisonStats(); drawComparison();
  } catch (e) { if (generation === compareGeneration) { $('compare-loading').textContent = 'Could not load the comparison. Try Refresh.'; toast(e.message); } }
}
$('compare-metric').addEventListener('change', () => { renderComparisonStats(); drawComparison(); });
function drawComparison() {
  const key = $('compare-metric').value;
  const series = sessionDetails.map((detail, i) => ({ name: detail.session.name, color: comparisonColors[i % comparisonColors.length], dashed: Math.floor(i / comparisonColors.length) % 2 === 1,
    points: detail.frames.map(f => ({ x: (Date.parse(f.time) - Date.parse(detail.session.started)) / 1000, y: f.metrics[key] ?? null })) }));
  const maxX = Math.max(60, ...series.map(s => s.points.at(-1)?.x || 0)); compareChart.update(series, [0, maxX], units[key], true);
}
function renderComparisonStats() {
  $('comparison-stats').replaceChildren(); $('comparison-info').replaceChildren(); const key = $('compare-metric').value;
  sessionDetails.forEach((detail, i) => {
    if (!detail) return;
    const legend = element('span', `${Math.floor(i / comparisonColors.length) % 2 ? '┄' : '●'} ${detail.session.name}`); legend.style.color = comparisonColors[i % comparisonColors.length]; $('comparison-info').append(legend);
    const box = element('div', null, 'stat-box'); box.style.borderTopColor = comparisonColors[i % comparisonColors.length]; box.append(element('h3', detail.session.name));
    box.append(element('p', [detail.session.cooler, detail.session.game, new Date(detail.session.started).toLocaleString()].filter(Boolean).join(' · '), 'hint'));
    const stat = detail.summary[key], values = element('div', null, 'stat-values');
    for (const [label, v] of [['Average', stat.average], ['Peak', stat.peak], ['Final 15 min', stat.final15Average]]) {
      const cell = element('div'); cell.append(element('span', label), element('strong', `${value(v, units[key] === 'RPM' ? 0 : 1)} ${units[key]}`)); values.append(cell);
    }
    box.append(values, element('p', `${stat.count.toLocaleString()} valid samples${detail.session.ambient != null ? ' · Room ' + detail.session.ambient + ' °C' : ' · Room temperature not recorded'}`, 'hint')); $('comparison-stats').append(box);
  });
}
async function init() {
  try {
    const info = await api('/api/info'); $('demo-banner').hidden = !info.demo;
    $('addresses').replaceChildren(...info.addresses.map(address => { const link = element('a', address); link.href = address; return link; }));
    await refreshSettings();
  } catch (e) { toast(e.message); }
  poll();
}
init();
