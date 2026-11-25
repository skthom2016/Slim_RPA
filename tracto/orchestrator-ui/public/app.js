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
        const delBadge = a.isDeleted
            ? `<span class="badge b-CANCELLED">Deleted</span>`
            : `<span class="badge b-SUCCEEDED">Active</span>`;
        const canRun = !a.isDeleted;
        // The name is what a person chose; the id is how the orchestrator addresses it. Artifacts
        // published before the name was stored have none, and show the id on its own.
        const nameCell = a.name
            ? `<div class="artifact-name">${esc(a.name)}</div>
               <div class="artifact-id">${esc(a.artifactId)}</div>`
            : `<div class="artifact-name artifact-name-missing">${esc(a.artifactId)}</div>`;
        return `
        <tr>
            <td>${nameCell}</td>
            <td><code>${esc(a.version)}</code></td>
            <td>${esc(a.schemaVersion)}</td>
            <td><code title="${a.contentHash}">${a.contentHash.substring(0, 14)}…</code></td>
            <td>${fmtDate(a.storedAt)}</td>
            <td>${delBadge}</td>
            <td class="row-actions">${canRun
                ? `<button class="btn btn-sm btn-primary" onclick="promptRun('${esc(a.artifactId)}','${esc(a.version)}')">▶ Run</button>
                   <button class="btn btn-sm btn-icon btn-danger" title="Delete this script"
                           onclick="promptDelete('${esc(a.artifactId)}','${esc(a.version)}','${esc(a.name || a.artifactId)}')">🗑</button>`
                : '—'}</td>
        </tr>`;
    }).join('');
}

// ── Delete artifact modal ─────────────────────────────────────────────────────
// Deleting is not undoable from this console, so it always goes through the modal.
let _delArtifactId = null, _delVersion = null;

function promptDelete(artifactId, version, label) {
    _delArtifactId = artifactId;
    _delVersion    = version;
    document.getElementById('delete-artifact-label').textContent =
        `Delete "${label}" version ${version}?`;
    document.getElementById('delete-overlay').classList.add('visible');
}

document.getElementById('delete-dismiss').addEventListener('click', () => {
    document.getElementById('delete-overlay').classList.remove('visible');
});

document.getElementById('delete-overlay').addEventListener('click', e => {
    if (e.target === e.currentTarget)
        document.getElementById('delete-overlay').classList.remove('visible');
});

document.getElementById('delete-confirm').addEventListener('click', async () => {
    document.getElementById('delete-overlay').classList.remove('visible');
    try {
        await api(`/artifacts/${encodeURIComponent(_delArtifactId)}/versions/${encodeURIComponent(_delVersion)}`,
            { method: 'DELETE' });
        toast(`Deleted ${_delArtifactId} ${_delVersion}`);
        loadArtifacts();
        loadOverview();
    } catch (e) {
        toast('Delete failed: ' + e.message, true);
    }
});

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

// What the desktop and browser runtimes warned or failed about, newest run first. These are the
// lines that say why a run failed; the timeline below only says that it did.
function renderRunLogs(logs) {
    if (!logs || logs.length === 0) {
        return `<div class="notice notice-info">
            No warnings or errors were recorded for this run.
            ${''/* A clean run is the normal case and worth saying plainly. */}
        </div>`;
    }

    return `<div class="log-list">` + logs.map(l => {
        const cls     = l.level === 'Error' ? 'log-error' : 'log-warn';
        const where   = [l.runtime, l.workerId].filter(Boolean).map(esc).join(' · ');
        const segment = l.segmentRunId ? ` · segment ${esc(shortId(l.segmentRunId))}` : '';
        return `
        <div class="log-row ${cls}">
            <div class="log-meta">
                <span class="log-level">${esc(l.level)}</span>
                <span class="log-time">${fmtDate(l.occurredAt)}</span>
                ${l.stepId ? `<span class="log-step">${esc(l.stepId)}</span>` : ''}
            </div>
            <div class="log-message">${esc(l.message)}</div>
            <div class="log-where">${where}${segment}</div>
        </div>`;
    }).join('') + `</div>`;
}

function renderTimeline(events) {
    if (!events || events.length === 0) {
        return `<div class="notice notice-warning">
            No lifecycle events for this run. The job may still be queued (no worker has picked
            it up yet), or the Run ID is wrong.
        </div>`;
    }

    return `<div class="timeline">` +
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

async function loadLogs(runId) {
    runId = (runId || document.getElementById('logs-run-id').value).trim();
    if (!runId) { toast('Paste a Run ID first', true); return; }

    document.getElementById('logs-run-id').value = runId;
    const level     = document.getElementById('logs-level-filter').value;
    const container = document.getElementById('logs-content');
    container.innerHTML = `<div class="notice notice-info">Loading…</div>`;

    // Both are fetched together: a run that failed has a reason in one and a shape in the other.
    const [logs, events] = await Promise.all([
        api(`/admin/logs/${encodeURIComponent(runId)}` + (level ? `?level=${encodeURIComponent(level)}` : ''))
            .catch(e => ({ error: e.message })),
        api(`/admin/telemetry/${encodeURIComponent(runId)}`).catch(e => ({ error: e.message })),
    ]);

    if (logs.error && events.error) {
        container.innerHTML = `<div class="notice notice-error">Error: ${esc(logs.error)}</div>`;
        return;
    }

    const logSection = logs.error
        ? `<div class="notice notice-error">Could not load run logs: ${esc(logs.error)}</div>`
        : renderRunLogs(logs.logs);

    container.innerHTML = `
        <h3 class="logs-heading">What the runtimes reported</h3>
        ${logSection}
        <h3 class="logs-heading">Run lifecycle</h3>
        ${events.error
            ? `<div class="notice notice-error">Could not load the timeline: ${esc(events.error)}</div>`
            : renderTimeline(events)}`;
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
        // Show every state, not just Queued: a job that a worker picks up straight away is
        // Running before the list first draws, and filtering to Queued hides it.
        switchTab('jobs', '');
    } catch (e) {
        toast('Failed to submit job: ' + e.message, true);
    }
});

// ── Schedules tab ─────────────────────────────────────────────────────────────
// Upcoming: every schedule, the one that fires soonest first. The server does the sorting; a
// schedule that is switched off has no next run and comes last. Completed: the runs schedules
// started that have finished, most recently finished first.
let _schedules    = [];     // the upcoming list as last loaded, for the edit form
let _artifactList = [];     // published scripts, for the form
let _schedEditing = null;   // the schedule being edited, or null when creating one

function relTime(iso) {
    if (!iso) return '';
    const secs = Math.round((new Date(iso).getTime() - Date.now()) / 1000);
    if (secs <= 0)  return 'due now';
    if (secs < 90)  return 'in ' + secs + ' s';
    const mins = Math.round(secs / 60);
    if (mins < 90)  return 'in ' + mins + ' min';
    const hrs = Math.floor(mins / 60), rest = mins % 60;
    if (hrs < 48)   return 'in ' + hrs + ' h' + (rest ? ' ' + rest + ' min' : '');
    return 'in ' + Math.round(hrs / 24) + ' days';
}

async function loadSchedules() {
    const view = document.getElementById('sched-view').value;
    document.getElementById('sched-upcoming-wrap').style.display  = view === 'upcoming'  ? '' : 'none';
    document.getElementById('sched-completed-wrap').style.display = view === 'completed' ? '' : 'none';
    document.getElementById('sched-limit-wrap').style.display     = view === 'completed' ? '' : 'none';
    if (view === 'completed') { await loadCompletedSchedules(); return; }

    let items;
    try { items = await api('/admin/schedules?view=upcoming'); }
    catch (e) { toast('Failed to load schedules: ' + e.message, true); return; }
    _schedules = items;

    const tbody = document.getElementById('sched-upcoming-body');
    document.getElementById('sched-count').textContent =
        items.length ? `${items.length} schedule${items.length === 1 ? '' : 's'}, soonest first` : '';
    if (items.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="8">No schedules yet. Use “New schedule” to create one.</td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(s => {
        const status = (s.enabled
            ? `<span class="badge b-AVAILABLE">SCHEDULED</span>`
            : `<span class="badge b-OFFLINE">DISABLED</span>`)
            + (s.activeRunState ? ` ${badge(s.activeRunState)}` : '');
        const next = s.enabled && s.nextRunAt
            ? `<div class="sched-when" title="${esc(s.nextRunAt)} (UTC)">${esc(s.nextRunLocal || fmtDate(s.nextRunAt))}</div>
               <div class="sched-sub">${esc(relTime(s.nextRunAt))}</div>`
            : '—';
        const last = s.lastFireAt
            ? `<div>${fmtDate(s.lastFireAt)}</div>
               <div class="sched-sub" title="${esc(s.lastError || '')}">${s.lastStatus ? badge(String(s.lastStatus).toUpperCase()) : ''}
                 ${s.lastRunId ? `<button class="link-id" onclick="openLogs('${esc(s.lastRunId)}')" title="${esc(s.lastRunId)}">${shortId(s.lastRunId)}</button>` : ''}</div>`
            : '—';
        return `
        <tr>
            <td>${next}</td>
            <td>${esc(s.artifactId)}</td>
            <td><code>${esc(s.version)}</code></td>
            <td class="sched-cron">${esc(s.cronExpression)}</td>
            <td>${esc(s.timeZone)}</td>
            <td style="white-space:nowrap">${status}</td>
            <td>${last}</td>
            <td class="row-actions">
                <button class="btn btn-sm btn-secondary" onclick="openScheduleModal('${esc(s.scheduleId)}')">Edit</button>
                <button class="btn btn-sm btn-icon btn-danger" title="Delete this schedule"
                        onclick="promptDeleteSchedule('${esc(s.scheduleId)}')">🗑</button>
            </td>
        </tr>`;
    }).join('');
}

async function loadCompletedSchedules() {
    const limit = parseInt(document.getElementById('sched-limit').value, 10) || 50;
    let data;
    try { data = await api('/admin/schedules/completed?limit=' + Math.min(Math.max(limit, 1), 200)); }
    catch (e) { toast('Failed to load completed runs: ' + e.message, true); return; }

    const tbody = document.getElementById('sched-completed-body');
    document.getElementById('sched-count').textContent =
        data.total ? `Showing ${data.items.length} of ${data.total}, most recently finished first` : '';
    if (!data.items || data.items.length === 0) {
        tbody.innerHTML = `<tr class="empty-row"><td colspan="7">No scheduled run has finished yet</td></tr>`;
        return;
    }
    tbody.innerHTML = data.items.map(r => {
        const sched = r.scheduleExists
            ? `<span class="sched-cron">${esc(r.cronExpression)}</span> <span class="sched-sub">${esc(r.timeZone)}</span>`
            : `<span class="sched-sub">schedule deleted</span>`;
        return `
        <tr>
            <td>${fmtDate(r.scheduledFor || r.queuedAt)}</td>
            <td>${fmtDate(r.finishedAt)}</td>
            <td>${esc(r.artifactId)}</td>
            <td><code>${esc(r.version)}</code></td>
            <td>${sched}</td>
            <td>${badge(r.state)}</td>
            <td><button class="link-id" onclick="openLogs('${esc(r.runId)}')" title="${esc(r.runId)}">${shortId(r.runId)}</button></td>
        </tr>`;
    }).join('');
}

// ── Schedule form (create and edit) ───────────────────────────────────────────
async function ensureArtifacts() {
    const data = await api('/artifacts');
    _artifactList = (data.artifacts || []).filter(a => !a.isDeleted);
}

function fillZones() {
    const list = document.getElementById('sched-zones');
    if (list.children.length) return;
    let zones;
    try { zones = Intl.supportedValuesOf('timeZone'); }
    catch { zones = ['Asia/Kolkata', 'Asia/Singapore', 'Europe/London', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney']; }
    if (!zones.includes('UTC')) zones.unshift('UTC');
    list.innerHTML = zones.map(z => `<option value="${esc(z)}"></option>`).join('');
}

function fillVersionOptions(artifactId, selected) {
    const sel = document.getElementById('sched-version');
    const versions = _artifactList.filter(a => a.artifactId === artifactId).map(a => a.version);
    if (selected && !versions.includes(selected)) versions.unshift(selected);
    sel.innerHTML = versions.map(v => `<option value="${esc(v)}">${esc(v)}</option>`).join('');
    sel.value = selected || versions[0] || '';
}

async function openScheduleModal(scheduleId) {
    try { await ensureArtifacts(); }
    catch (e) { toast('Failed to load scripts: ' + e.message, true); return; }
    fillZones();

    const s = scheduleId ? _schedules.find(x => x.scheduleId === scheduleId) : null;
    _schedEditing = s || null;
    const artSel = document.getElementById('sched-artifact');

    if (s) {
        document.getElementById('sched-modal-title').textContent = 'Edit Schedule';
        document.getElementById('sched-modal-sub').textContent   = s.scheduleId;
        const label = (_artifactList.find(a => a.artifactId === s.artifactId) || {}).name || s.artifactId;
        artSel.innerHTML = `<option value="${esc(s.artifactId)}">${esc(label)}</option>`;
        artSel.disabled  = true;               // a schedule's script cannot be changed, only its version
        fillVersionOptions(s.artifactId, s.version);
        document.getElementById('sched-cron').value    = s.cronExpression;
        document.getElementById('sched-zone').value    = s.timeZone;
        document.getElementById('sched-overlap').value = s.overlapPolicy;
        document.getElementById('sched-grace').value   = s.graceSeconds;
        document.getElementById('sched-enabled').checked = s.enabled;
        document.getElementById('sched-params').value  =
            Object.keys(s.params || {}).length ? JSON.stringify(s.params, null, 2) : '';
    } else {
        document.getElementById('sched-modal-title').textContent = 'New Schedule';
        document.getElementById('sched-modal-sub').textContent   = 'Runs a published script on a timetable.';
        const ids = [...new Set(_artifactList.map(a => a.artifactId))];
        artSel.innerHTML = ids.map(id => {
            const a = _artifactList.find(x => x.artifactId === id);
            return `<option value="${esc(id)}">${esc(a.name || id)}</option>`;
        }).join('');
        artSel.disabled = false;
        fillVersionOptions(artSel.value, null);
        document.getElementById('sched-cron').value    = '';
        document.getElementById('sched-zone').value    = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
        document.getElementById('sched-overlap').value = 'skip';
        document.getElementById('sched-grace').value   = 900;
        document.getElementById('sched-enabled').checked = true;
        document.getElementById('sched-params').value  = '';
    }
    document.getElementById('sched-error').textContent = '';
    document.getElementById('sched-overlay').classList.add('visible');
    document.getElementById('sched-cron').focus();
}

function closeScheduleModal() {
    document.getElementById('sched-overlay').classList.remove('visible');
    _schedEditing = null;
}

function sameParams(a, b) {
    const ka = Object.keys(a || {}).sort(), kb = Object.keys(b || {}).sort();
    return ka.length === kb.length && ka.every((k, i) => k === kb[i] && a[k] === b[k]);
}

async function saveSchedule() {
    const err = document.getElementById('sched-error');
    err.textContent = '';

    let params = {};
    const raw = document.getElementById('sched-params').value.trim();
    if (raw) {
        try { params = JSON.parse(raw); }
        catch { err.textContent = 'Parameters must be valid JSON, for example {"sheetName": "Sheet1"}.'; return; }
        if (params === null || typeof params !== 'object' || Array.isArray(params) ||
            Object.values(params).some(v => typeof v !== 'string')) {
            err.textContent = 'Parameters must be a JSON object whose values are all text, for example {"sheetName": "Sheet1"}.';
            return;
        }
    }

    const form = {
        cronExpression: document.getElementById('sched-cron').value.trim(),
        timeZone:       document.getElementById('sched-zone').value.trim(),
        version:        document.getElementById('sched-version').value,
        overlapPolicy:  document.getElementById('sched-overlap').value,
        graceSeconds:   parseInt(document.getElementById('sched-grace').value, 10),
        enabled:        document.getElementById('sched-enabled').checked,
        params,
    };
    if (!form.cronExpression) { err.textContent = 'Enter a cron expression.'; return; }
    if (!form.timeZone)       { err.textContent = 'Enter a time zone, for example Asia/Kolkata.'; return; }
    if (Number.isNaN(form.graceSeconds)) { err.textContent = 'Grace must be a number of seconds.'; return; }

    try {
        if (_schedEditing) {
            // Send only what changed: changing the expression or the time zone restarts the next
            // run from now, which editing a parameter should not do.
            const cur = _schedEditing, patch = {};
            if (form.cronExpression !== cur.cronExpression) patch.cronExpression = form.cronExpression;
            if (form.timeZone       !== cur.timeZone)       patch.timeZone       = form.timeZone;
            if (form.version        !== cur.version)        patch.version        = form.version;
            if (form.overlapPolicy  !== cur.overlapPolicy)  patch.overlapPolicy  = form.overlapPolicy;
            if (form.graceSeconds   !== cur.graceSeconds)   patch.graceSeconds   = form.graceSeconds;
            if (form.enabled        !== cur.enabled)        patch.enabled        = form.enabled;
            if (!sameParams(form.params, cur.params))       patch.params         = form.params;
            if (Object.keys(patch).length === 0) { closeScheduleModal(); toast('No changes to save'); return; }
            await api('/admin/schedules/' + encodeURIComponent(cur.scheduleId),
                      { method: 'PATCH', body: JSON.stringify(patch) });
        } else {
            await api('/admin/schedules', {
                method: 'POST',
                body: JSON.stringify({ ...form, artifactId: document.getElementById('sched-artifact').value }),
            });
        }
    } catch (e) {
        err.textContent = e.message;            // the server's own words: a bad cron, an unknown zone
        return;
    }
    closeScheduleModal();
    toast('Schedule saved');
    loadSchedules();
}

// ── Delete schedule ───────────────────────────────────────────────────────────
let _delScheduleId = null;

function promptDeleteSchedule(scheduleId) {
    const s = _schedules.find(x => x.scheduleId === scheduleId);
    _delScheduleId = scheduleId;
    document.getElementById('sched-delete-label').textContent = s
        ? `${s.artifactId} ${s.version}, ${s.cronExpression} (${s.timeZone})`
        : scheduleId;
    document.getElementById('sched-delete-overlay').classList.add('visible');
}

function closeDeleteSchedule() {
    document.getElementById('sched-delete-overlay').classList.remove('visible');
    _delScheduleId = null;
}

async function confirmDeleteSchedule() {
    if (!_delScheduleId) return;
    const id = _delScheduleId;
    try { await api('/admin/schedules/' + encodeURIComponent(id), { method: 'DELETE' }); }
    catch (e) { toast('Delete failed: ' + e.message, true); return; }
    closeDeleteSchedule();
    toast('Schedule deleted');
    loadSchedules();
}

// ── Tab switching ─────────────────────────────────────────────────────────────
const TAB_TITLES = {
    overview: 'Overview', jobs: 'Jobs', schedules: 'Schedules', artifacts: 'Artifacts', workers: 'Workers', logs: 'Logs'
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
        case 'schedules': loadSchedules(); break;
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

document.getElementById('sched-view').addEventListener('change', loadSchedules);
document.getElementById('sched-limit').addEventListener('change', loadSchedules);
document.getElementById('btn-sched-refresh').addEventListener('click', loadSchedules);
document.getElementById('btn-sched-new').addEventListener('click', () => openScheduleModal(null));
document.getElementById('sched-save').addEventListener('click', saveSchedule);
document.getElementById('sched-dismiss').addEventListener('click', closeScheduleModal);
document.getElementById('sched-overlay').addEventListener('click', e => {
    if (e.target === document.getElementById('sched-overlay')) closeScheduleModal();
});
document.getElementById('sched-artifact').addEventListener('change', e => fillVersionOptions(e.target.value, null));
document.getElementById('sched-delete-dismiss').addEventListener('click', closeDeleteSchedule);
document.getElementById('sched-delete-confirm').addEventListener('click', confirmDeleteSchedule);
document.getElementById('sched-delete-overlay').addEventListener('click', e => {
    if (e.target === document.getElementById('sched-delete-overlay')) closeDeleteSchedule();
});

document.getElementById('jobs-state-filter').addEventListener('change', loadJobs);
document.getElementById('artifacts-show-deleted').addEventListener('change', loadArtifacts);

document.getElementById('logs-run-id').addEventListener('keydown', e => {
    if (e.key === 'Enter') loadLogs();
});

document.getElementById('logs-level-filter').addEventListener('change', () => {
    if (document.getElementById('logs-run-id').value.trim()) loadLogs();
});

// ── Boot ──────────────────────────────────────────────────────────────────────
checkConn();
loadOverview();
