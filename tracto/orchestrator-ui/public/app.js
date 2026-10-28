'use strict';

// ── API ───────────────────────────────────────────────────────────────────────
async function api(path, opts = {}) {
    const res = await fetch('/api' + path, {
        headers: { 'Content-Type': 'application/json', ...opts.headers },
        ...opts,
    });
    const body = await res.json().catch(() => ({}));
    if (!res.ok) throw Object.assign(new Error(body.message || res.statusText), { status: res.status, body });
    return body;
}

// ── Toast ─────────────────────────────────────────────────────────────────────
function toast(msg, isError = false) {
    const area = document.getElementById('toast-area');
    const el   = document.createElement('div');
    el.className = 'toast' + (isError ? ' toast-error' : '');
    el.textContent = msg;
    area.appendChild(el);
    setTimeout(() => el.remove(), 4000);
}

// ── Formatters ────────────────────────────────────────────────────────────────
function shortId(id) {
    return id ? id.substring(0, 8) + '…' : '—';
}

function fmtDate(iso) {
    if (!iso) return '—';
    try {
        const d = new Date(iso);
        return d.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'medium' });
    } catch { return iso; }
}

function badge(state) {
    const cls = {
        QUEUED: 'b-QUEUED', RUNNING: 'b-RUNNING', SUCCEEDED: 'b-SUCCEEDED',
        FAILED: 'b-FAILED', CANCELLED: 'b-CANCELLED', TIMEDOUT: 'b-TIMEDOUT',
        UNKNOWN: 'b-UNKNOWN', CREATED: 'b-CREATED', LEASED: 'b-LEASED',
        AVAILABLE: 'b-AVAILABLE', OFFLINE: 'b-OFFLINE',
    }[state] || 'b-default';
    return `<span class="badge ${cls}">${state}</span>`;
}

// ── Connection status ─────────────────────────────────────────────────────────
async function checkConn() {
    const el    = document.getElementById('conn-status');
    const label = document.getElementById('conn-label');
    try {
        await api('/health');
        el.className    = 'conn-status ok';
        label.textContent = 'Connected';
    } catch {
        el.className    = 'conn-status err';
        label.textContent = 'Disconnected';
    }
}

// ── Overview tab ──────────────────────────────────────────────────────────────
async function loadOverview() {
    let jobs = { jobs: [], count: 0 }, workers = [], artifacts = { artifacts: [], count: 0 };

    await Promise.allSettled([
        api('/jobs?limit=200').then(d => { jobs = d; }),
        api('/workers').then(d => { workers = d || []; }),
        api('/artifacts').then(d => { artifacts = d; }),
    ]);

    const counts = {};
    let needsAtt = 0;
    for (const j of jobs.jobs || []) {
        counts[j.state] = (counts[j.state] || 0) + 1;
        if (j.needsAttention) needsAtt++;
    }

    const online = workers.filter(w => w.isOnline).length;

    const cards = [
        { label: 'Queued',          value: counts.QUEUED    || 0, cls: 'c-queued',    tab: 'jobs',      filter: 'Queued'    },
        { label: 'Running',         value: counts.RUNNING   || 0, cls: 'c-running',   tab: 'jobs',      filter: 'Running'   },
        { label: 'Succeeded',       value: counts.SUCCEEDED || 0, cls: 'c-succeeded', tab: 'jobs',      filter: 'Succeeded' },
        { label: 'Failed',          value: counts.FAILED    || 0, cls: 'c-failed',    tab: 'jobs',      filter: 'Failed'    },
        { label: 'Needs Attention', value: needsAtt,               cls: 'c-needs-att', tab: 'jobs',      filter: ''          },
        { label: 'Total Jobs',      value: jobs.count        || 0, cls: 'c-total',     tab: 'jobs',      filter: ''          },
        { label: 'Workers Online',  value: online,                 cls: 'c-workers',   tab: 'workers',   filter: ''          },
        { label: 'Artifacts',       value: artifacts.count   || 0, cls: 'c-artifacts', tab: 'artifacts', filter: ''          },
    ];

    document.getElementById('stat-grid').innerHTML = cards.map(c => `
        <div class="stat-card ${c.cls}" data-goto-tab="${c.tab}" data-goto-filter="${c.filter}" role="button" tabindex="0">
            <div class="stat-value">${c.value}</div>
            <div class="stat-label">${c.label}</div>
        </div>
    `).join('');

    document.querySelectorAll('[data-goto-tab]').forEach(el => {
        el.addEventListener('click', () => switchTab(el.dataset.gotoTab, el.dataset.gotoFilter));
    });
}

// ── Jobs tab ──────────────────────────────────────────────────────────────────
async function loadJobs() {
    const state = document.getElementById('jobs-state-filter').value;
    const limit = parseInt(document.getElementById('jobs-limit').value, 10) || 50;

    let url = `/jobs?limit=${limit}`;
    if (state) url += `&state=${encodeURIComponent(state)}`;

    let data;
    try { data = await api(url); }
    catch (e) { toast('Failed to load jobs: ' + e.message, true); return; }

    const tbody = document.getElementById('jobs-body');

    if (!data.jobs || data.jobs.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="9">No jobs found</td></tr>`;
        return;
    }

    tbody.innerHTML = data.jobs.map(j => {
        const canCancel = j.state === 'QUEUED' || j.state === 'RUNNING';
        const attFlag   = j.needsAttention ? `<span class="badge b-FAILED" title="Needs attention">⚠ Yes</span>` : '—';
        return `
        <tr>
            <td>
                <button class="link-id" onclick="openLogs('${j.runId}')" title="${j.runId}">
                    ${shortId(j.runId)}
                </button>
            </td>
            <td>${esc(j.scriptArtifactId)}</td>
            <td><code>${esc(j.scriptVersion)}</code></td>
            <td>${badge(j.state)}</td>
            <td>${j.workerId ? esc(j.workerId) : '—'}</td>
            <td>${esc(j.triggerType || '—')}</td>
            <td>${attFlag}</td>
            <td>${fmtDate(j.createdAt)}</td>
            <td style="white-space:nowrap">
                ${canCancel ? `<button class="btn btn-sm btn-danger" onclick="promptCancel('${j.runId}')">Cancel</button> ` : ''}
                <button class="btn btn-sm btn-secondary" onclick="openLogs('${j.runId}')">Logs</button>
            </td>
        </tr>`;
    }).join('');
}

// ── Artifacts tab ─────────────────────────────────────────────────────────────
async function loadArtifacts() {
    const incDel = document.getElementById('artifacts-show-deleted').checked;
    let url = '/artifacts' + (incDel ? '?includeDeleted=true' : '');

    let data;
    try { data = await api(url); }
    catch (e) { toast('Failed to load artifacts: ' + e.message, true); return; }

    const tbody = document.getElementById('artifacts-body');

    if (!data.artifacts || data.artifacts.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="7">No artifacts uploaded yet</td></tr>`;
        return;
    }

    tbody.innerHTML = data.artifacts.map(a => {
        const sigBadge = a.signatureValid
            ? `<span class="badge b-SIGNED">✓ Signed</span>`
            : `<span class="badge b-UNSIGNED">Unsigned</span>`;
        const delBadge = a.isDeleted
            ? `<span class="badge b-CANCELLED">Deleted</span>`
            : `<span class="badge b-SUCCEEDED">Active</span>`;
        const canRun = !a.isDeleted;
        return `
        <tr>
            <td>${esc(a.artifactId)}</td>
            <td><code>${esc(a.version)}</code></td>
            <td>${esc(a.schemaVersion)}</td>
            <td>${sigBadge}</td>
            <td><code title="${a.contentHash}">${a.contentHash.substring(0, 14)}…</code></td>
            <td>${fmtDate(a.storedAt)}</td>
            <td>${delBadge}</td>
            <td>${canRun ? `<button class="btn btn-sm btn-primary" onclick="promptRun('${esc(a.artifactId)}','${esc(a.version)}')">▶ Run</button>` : '—'}</td>
        </tr>`;
    }).join('');
}

// ── Workers tab ───────────────────────────────────────────────────────────────
async function loadWorkers() {
    let workers;
    try { workers = await api('/workers'); }
    catch (e) { toast('Failed to load workers: ' + e.message, true); return; }

    const tbody = document.getElementById('workers-body');

    if (!workers || workers.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="7">No workers registered</td></tr>`;
        return;
    }

    tbody.innerHTML = workers.map(w => {
        const onlineIcon = w.isOnline
            ? `<span class="online-dot" title="Online">●</span>`
            : `<span class="offline-dot" title="Offline">●</span>`;
        return `
        <tr>
            <td>${esc(w.workerId)}</td>
            <td>${esc(w.runtimeType)}</td>
            <td>${badge(w.state)}</td>
            <td>${onlineIcon}</td>
            <td>${fmtDate(w.lastHeartbeatAt)}</td>
            <td>${fmtDate(w.registeredAt)}</td>
            <td>
                ${w.isOnline
                    ? `<button class="btn btn-sm btn-danger" onclick="markOffline('${esc(w.workerId)}')">Mark Offline</button>`
                    : '—'}
            </td>
        </tr>`;
    }).join('');
}

async function markOffline(workerId) {
    if (!confirm(`Mark "${workerId}" as offline?`)) return;
    try {
        await api(`/workers/${encodeURIComponent(workerId)}/offline`, { method: 'POST', body: '{}' });
        toast(`Worker ${workerId} marked offline`);
        loadWorkers();
    } catch (e) {
        toast('Error: ' + e.message, true);
    }
}

// ── Logs tab ──────────────────────────────────────────────────────────────────
function tlDotClass(eventType) {
    const t = eventType.toUpperCase();
    if (t.includes('SUCCEED') || t.includes('COMPLET') || t.includes('REGIST')) return 'tl-green';
    if (t.includes('FAIL') || t.includes('ERROR') || t.includes('TIMED'))        return 'tl-red';
    if (t.includes('CANCEL'))                                                     return 'tl-orange';
    if (t.includes('RUNNING') || t.includes('START') || t.includes('ACCEPT'))    return 'tl-blue';
    return 'tl-gray';
}

async function loadLogs(runId) {
    runId = (runId || document.getElementById('logs-run-id').value).trim();
    if (!runId) { toast('Paste a Run ID first', true); return; }

    document.getElementById('logs-run-id').value = runId;
    const container = document.getElementById('logs-content');
    container.innerHTML = `<div class="notice notice-info">Loading…</div>`;

    let events;
    try { events = await api(`/admin/telemetry/${encodeURIComponent(runId)}`); }
    catch (e) { container.innerHTML = `<div class="notice notice-error">Error: ${esc(e.message)}</div>`; return; }

    if (!events || events.length === 0) {
        container.innerHTML = `<div class="notice notice-warning">
            No telemetry events found for this run.<br>
            The job may still be queued (no worker picked it up yet), or the Run ID is incorrect.
        </div>`;
        return;
    }

    container.innerHTML = `<div class="timeline">` +
        events.map((ev, i) => {
            const dotCls = tlDotClass(ev.eventType);
            const isLast = i === events.length - 1;
            return `
            <div class="timeline-item">
                ${!isLast ? '<div class="tl-track"></div>' : ''}
                <div class="tl-dot ${dotCls}"></div>
                <div class="tl-body">
                    <div class="tl-event">${esc(ev.eventType)}</div>
                    <div class="tl-time">${fmtDate(ev.occurredAt)}${ev.workerId ? ' · Worker: ' + esc(ev.workerId) : ''}</div>
                    ${ev.metadata ? `<pre class="tl-meta">${esc(ev.metadata)}</pre>` : ''}
                </div>
            </div>`;
        }).join('') + `</div>`;
}

function openLogs(runId) {
    switchTab('logs');
    loadLogs(runId);
}

// ── Cancel modal ──────────────────────────────────────────────────────────────
let _cancelRunId = null;

function promptCancel(runId) {
    _cancelRunId = runId;
    document.getElementById('cancel-run-id-label').textContent = 'Run ID: ' + runId;
    document.getElementById('cancel-requested-by').value = '';
    document.getElementById('cancel-overlay').classList.add('visible');
    setTimeout(() => document.getElementById('cancel-requested-by').focus(), 50);
}

document.getElementById('cancel-dismiss').addEventListener('click', () => {
    document.getElementById('cancel-overlay').classList.remove('visible');
});

document.getElementById('cancel-confirm').addEventListener('click', async () => {
    const by = document.getElementById('cancel-requested-by').value.trim();
    if (!by) { toast('Please enter your name or reason', true); return; }

    document.getElementById('cancel-overlay').classList.remove('visible');

    try {
        await api(`/jobs/${encodeURIComponent(_cancelRunId)}/cancel`, {
            method: 'POST',
            body: JSON.stringify({ requestedBy: by }),
        });
        toast('Job cancelled');
        loadJobs();
        loadOverview();
    } catch (e) {
        toast('Cancel failed: ' + e.message, true);
    }
});

// Close modal on overlay click
document.getElementById('cancel-overlay').addEventListener('click', e => {
    if (e.target === e.currentTarget)
        document.getElementById('cancel-overlay').classList.remove('visible');
});

// ── Run modal ─────────────────────────────────────────────────────────────
let _runArtifactId = null, _runVersion = null;

function promptRun(artifactId, version) {
    _runArtifactId = artifactId;
    _runVersion    = version;
    document.getElementById('run-artifact-label').textContent =
        `Artifact: ${artifactId}  ·  Version: ${version}`;
    document.getElementById('run-overlay').classList.add('visible');
}

document.getElementById('run-dismiss').addEventListener('click', () => {
    document.getElementById('run-overlay').classList.remove('visible');
});

document.getElementById('run-overlay').addEventListener('click', e => {
    if (e.target === e.currentTarget)
        document.getElementById('run-overlay').classList.remove('visible');
});

document.getElementById('run-confirm').addEventListener('click', async () => {
    const triggerType = document.getElementById('run-trigger-type').value;
    document.getElementById('run-overlay').classList.remove('visible');
    try {
        const result = await api('/jobs', {
            method: 'POST',
            body: JSON.stringify({
                schemaVersion:    '1',
                scriptArtifactId: _runArtifactId,
                scriptVersion:    _runVersion,
                triggerType,
            }),
        });
        toast(`Job queued — Run ID: ${result.runId || '?'}`);
        switchTab('jobs', 'Queued');
    } catch (e) {
        toast('Failed to submit job: ' + e.message, true);
    }
});

// ── Tab switching ─────────────────────────────────────────────────────────────
const TAB_TITLES = {
    overview: 'Overview', jobs: 'Jobs', artifacts: 'Artifacts', workers: 'Workers', logs: 'Logs'
};
let currentTab = 'overview';

function switchTab(tab, stateFilter) {
    currentTab = tab;

    document.querySelectorAll('.nav-link').forEach(l =>
        l.classList.toggle('active', l.dataset.tab === tab));
    document.querySelectorAll('.tab-pane').forEach(p =>
        p.classList.toggle('active', p.id === 'tab-' + tab));

    document.getElementById('page-title').textContent = TAB_TITLES[tab] || tab;

    if (stateFilter !== undefined) {
        const sel = document.getElementById('jobs-state-filter');
        if (sel) sel.value = stateFilter;
    }

    refreshTab(tab);
}

function refreshTab(tab) {
    switch (tab) {
        case 'overview':  loadOverview();  break;
        case 'jobs':      loadJobs();      break;
        case 'artifacts': loadArtifacts(); break;
        case 'workers':   loadWorkers();   break;
    }
}

// ── XSS helper ────────────────────────────────────────────────────────────────
function esc(s) {
    if (s == null) return '';
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// ── Auto-refresh ──────────────────────────────────────────────────────────────
setInterval(() => {
    checkConn();
    if (currentTab !== 'logs') refreshTab(currentTab);
}, 5000);

// ── Wire up buttons ───────────────────────────────────────────────────────────
document.querySelectorAll('.nav-link').forEach(link => {
    link.addEventListener('click', e => { e.preventDefault(); switchTab(link.dataset.tab); });
});

document.getElementById('btn-refresh').addEventListener('click',           () => { checkConn(); refreshTab(currentTab); });
document.getElementById('btn-jobs-refresh').addEventListener('click',      loadJobs);
document.getElementById('btn-artifacts-refresh').addEventListener('click', loadArtifacts);
document.getElementById('btn-workers-refresh').addEventListener('click',   loadWorkers);
document.getElementById('btn-logs-load').addEventListener('click',         () => loadLogs());
document.getElementById('btn-logs-clear').addEventListener('click',        () => {
    document.getElementById('logs-run-id').value = '';
    document.getElementById('logs-content').innerHTML = '';
});

document.getElementById('jobs-state-filter').addEventListener('change', loadJobs);
document.getElementById('artifacts-show-deleted').addEventListener('change', loadArtifacts);

document.getElementById('logs-run-id').addEventListener('keydown', e => {
    if (e.key === 'Enter') loadLogs();
});

// ── Boot ──────────────────────────────────────────────────────────────────────
checkConn();
loadOverview();
