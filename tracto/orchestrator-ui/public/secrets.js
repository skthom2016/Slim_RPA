'use strict';

// Secrets, groups with environment variables, users and the password page. Loaded after app.js, which provides
// api(), toast(), esc(), fmtDate(), switchTab() and the tab bookkeeping.

const $ = id => document.getElementById(id);

// ── Who is signed in ──────────────────────────────────────────────────────────
let me = { username: '', role: '', permissions: [], allowedGroups: null };
const can = perm => me.permissions.includes(perm);

function applyPermissions() {
    document.querySelectorAll('[data-perm]').forEach(el => { el.hidden = !can(el.dataset.perm); });
    $('user-chip').textContent = `👤 ${me.username} · ${me.role}`;
}

async function loadMe() {
    me = await api('/auth/me');
    applyPermissions();
}

// ── Small helpers ─────────────────────────────────────────────────────────────
function showModal(id)  { $(id).classList.add('visible'); }
function hideModal(id)  { $(id).classList.remove('visible'); }
function setErr(id, msg) { $(id).textContent = msg || ''; }

// An error from api() as one line.
const msgOf = err => (err && err.message) || 'Something went wrong';

// The eye next to a password field shows what was typed.
document.addEventListener('click', e => {
    const eye = e.target.closest('[data-eye]');
    if (!eye) return;
    const input = $(eye.dataset.eye);
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    eye.title = show ? 'Hide what was typed' : 'Show what was typed';
});

function csvList(text) {
    return text.split(',').map(s => s.trim()).filter(Boolean);
}

// ── Confirm dialog ────────────────────────────────────────────────────────────
let confirmAction = null;
function confirmDialog(title, desc, buttonText, action) {
    $('confirm-title').textContent = title;
    $('confirm-desc').textContent = desc;
    $('confirm-go').textContent = buttonText;
    setErr('confirm-error', '');
    confirmAction = action;
    showModal('confirm-overlay');
}
$('confirm-dismiss').addEventListener('click', () => hideModal('confirm-overlay'));
$('confirm-go').addEventListener('click', async () => {
    try { await confirmAction(); hideModal('confirm-overlay'); }
    catch (err) { setErr('confirm-error', msgOf(err)); }
});

// ══ Secrets ══════════════════════════════════════════════════════════════════
let secrets = [];
let expiryFilter = false;     // show only secrets that expire soon or have expired
const revealed = new Map();   // secret name → timer that hides its value again
const REVEAL_MS = 30000;
const STARS = '●●●●●●●●';

function maskedValue(name) {
    const cell = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] .pw`);
    if (cell) { cell.textContent = STARS; cell.classList.remove('shown'); }
    const eye = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] [data-act="reveal"]`);
    if (eye) eye.title = 'Show the value';
}

function hideValue(name) {
    clearTimeout(revealed.get(name));
    revealed.delete(name);
    maskedValue(name);
}

function hideAllValues() {
    [...revealed.keys()].forEach(hideValue);
}

// The value disappears when the page is left, as well as after 30 seconds.
window.addEventListener('blur', hideAllValues);
document.addEventListener('visibilitychange', () => { if (document.hidden) hideAllValues(); });

function expiryCell(c) {
    switch (c.status) {
        case 'none':    return '<span class="muted">No expiry</span>';
        case 'soon':    return `<span class="exp-soon" title="Expires soon">⚠ ${esc(c.expiresOn)}</span>`;
        case 'expired': return `<span class="exp-expired" title="Expired">⛔ ${esc(c.expiresOn)} · Expired</span>`;
        default:        return esc(c.expiresOn);
    }
}

function usedByCell(c) {
    const used = c.usedBy || [];
    if (!used.length) return '<span class="muted">not used</span>';
    return used.map(u => `<code title="Group ${esc(u.group)}">${esc(u.fullName || (u.group + ' / ' + u.variable))}</code>`).join(' ');
}

function renderSecrets() {
    const body = $('secret-body');
    const q = $('secret-search').value.trim().toLowerCase();
    const rows = secrets.filter(c =>
        (!expiryFilter || c.status === 'soon' || c.status === 'expired') &&
        (!q || c.name.toLowerCase().includes(q) || (c.description || '').toLowerCase().includes(q)));
    if (!rows.length) {
        body.innerHTML = `<tr class="empty-row"><td colspan="7">${secrets.length ? 'Nothing matches' : 'No secrets yet'}</td></tr>`;
        return;
    }
    const reveal = can('credentials.reveal');
    const edit = can('credentials.edit');
    body.innerHTML = rows.map(c => {
        const val = `<span class="pw-cell" data-name="${esc(c.name)}"><span class="pw">${STARS}</span>` +
            (reveal ? ` <button class="icon-btn" data-act="reveal" data-name="${esc(c.name)}" title="Show the value" aria-label="Show the value">👁</button>` +
                      ` <button class="icon-btn" data-act="copy" data-name="${esc(c.name)}" title="Copy the value" aria-label="Copy the value">⧉</button>` : '') + '</span>';
        const kind = c.kind === 'totp' ? '<span class="badge b-default" title="A one-time-code key: scripts get the current code">code key</span>' : 'Text';
        const actions = `<button class="btn btn-secondary btn-sm" data-act="history" data-name="${esc(c.name)}">History</button>` +
            (edit ? ` <button class="btn btn-secondary btn-sm" data-act="edit" data-name="${esc(c.name)}" title="Edit">✎</button>` +
                    ` <button class="btn btn-outline-danger btn-sm" data-act="delete" data-name="${esc(c.name)}" title="Delete">🗑</button>` : '');
        return `<tr><td><strong>${esc(c.name)}</strong></td><td>${kind}</td><td>${val}</td><td>${esc(c.description)}</td>` +
               `<td>${expiryCell(c)}</td><td>${usedByCell(c)}</td><td class="actions">${actions}</td></tr>`;
    }).join('');
}

function renderSecretSummary() {
    const soon = secrets.filter(c => c.status === 'soon').length;
    const expired = secrets.filter(c => c.status === 'expired').length;
    const parts = [];
    if (soon)    parts.push(`⚠ ${soon} expiring soon`);
    if (expired) parts.push(`⛔ ${expired} expired`);
    const el = $('cred-summary');
    el.innerHTML = parts.length
        ? `<button class="link-btn${expiryFilter ? ' active' : ''}" id="cred-summary-btn" title="Show only these">${esc(parts.join(' · '))}${expiryFilter ? ' (showing only these)' : ''}</button>`
        : '';
    const btn = $('cred-summary-btn');
    if (btn) btn.addEventListener('click', () => { expiryFilter = !expiryFilter; renderSecretSummary(); renderSecrets(); });
    else expiryFilter = false;
}

async function loadSecretsPage() {
    hideAllValues();
    try {
        const out = await api('/admin/secrets');
        $('cred-locked').style.display = 'none';
        secrets = out.secrets || [];
        renderSecretSummary();
        renderSecrets();
    } catch (err) {
        if (err.body && err.body.error === 'CREDENTIAL_STORE_LOCKED') {
            $('cred-locked').style.display = '';
            $('secret-body').innerHTML = '<tr class="empty-row"><td colspan="7">The secret store is locked</td></tr>';
            $('cred-summary').innerHTML = '';
        } else {
            toast(msgOf(err), true);
        }
    }
}

// ── reveal, copy and the row buttons ──────────────────────────────────────────
async function secretValue(name, how) {
    const out = await api(`/admin/secrets/${encodeURIComponent(name)}/${how}`, { method: 'POST', body: JSON.stringify({}) });
    return out.value;
}

$('secret-body').addEventListener('click', async e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const name = btn.dataset.name;
    try {
        switch (btn.dataset.act) {
            case 'reveal': {
                if (revealed.has(name)) { hideValue(name); return; }
                const value = await secretValue(name, 'reveal');
                const cell = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] .pw`);
                if (!cell) return;
                cell.textContent = value;
                cell.classList.add('shown');
                btn.title = 'Hide the value';
                revealed.set(name, setTimeout(() => hideValue(name), REVEAL_MS));
                break;
            }
            case 'copy': {
                const value = await secretValue(name, 'copy');
                await navigator.clipboard.writeText(value);
                toast(`Value of ${name} copied`);
                break;
            }
            case 'history': openHistory(name); break;
            case 'edit':    openSecretModal(secrets.find(c => c.name === name)); break;
            case 'delete':
                confirmDialog('Delete secret', `Delete the secret ${name}? Its history is kept. A secret that a group variable still uses cannot be deleted.`, 'Delete', async () => {
                    await api(`/admin/secrets/${encodeURIComponent(name)}`, { method: 'DELETE' });
                    toast(`Deleted ${name}`);
                    loadSecretsPage();
                });
                break;
        }
    } catch (err) { toast(msgOf(err), true); }
});

let searchTimer = null;
$('secret-search').addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(renderSecrets, 150); });
$('btn-secret-refresh').addEventListener('click', loadSecretsPage);

// ── add and edit ──────────────────────────────────────────────────────────────
let editingSecret = null;

function setExpiryInputs(expiresOn) {
    $('secret-expiry-none').checked = !expiresOn;
    $('secret-expiry-date').checked = !!expiresOn;
    $('secret-expires').value = expiresOn || '';
    $('secret-expires').disabled = !expiresOn;
}
document.querySelectorAll('input[name="secret-expiry"]').forEach(r => r.addEventListener('change', () => {
    $('secret-expires').disabled = !$('secret-expiry-date').checked;
    if ($('secret-expiry-date').checked && !$('secret-expires').value) $('secret-expires').focus();
}));

function openSecretModal(c) {
    editingSecret = c || null;
    setErr('secret-error', '');
    $('secret-modal-title').textContent = c ? `Edit ${c.name}` : 'Add a secret';
    $('secret-name').value = c ? c.name : '';
    $('secret-name').disabled = !!c;
    $('secret-kind').value = c ? c.kind : 'text';
    $('secret-kind').disabled = !!c;
    $('secret-value').value = '';
    $('secret-value').type = 'password';
    $('secret-value-hint').textContent = c ? 'Leave empty to keep the value as it is.' : '';
    $('secret-desc').value = c ? (c.description || '') : '';
    setExpiryInputs(c ? c.expiresOn : null);
    showModal('secret-overlay');
    (c ? $('secret-value') : $('secret-name')).focus();
}

$('btn-secret-add').addEventListener('click', () => openSecretModal(null));
$('secret-dismiss').addEventListener('click', () => { $('secret-value').value = ''; hideModal('secret-overlay'); });

$('secret-save').addEventListener('click', async () => {
    setErr('secret-error', '');
    const expiresOn = $('secret-expiry-date').checked ? $('secret-expires').value : null;
    if ($('secret-expiry-date').checked && !expiresOn) { setErr('secret-error', 'Pick the expiry date, or choose No expiry.'); return; }
    try {
        if (editingSecret) {
            const body = { description: $('secret-desc').value.trim(), expiresOn };
            if ($('secret-value').value) body.value = $('secret-value').value;
            await api(`/admin/secrets/${encodeURIComponent(editingSecret.name)}`, { method: 'PUT', body: JSON.stringify(body) });
            toast(`Saved ${editingSecret.name}`);
        } else {
            const body = {
                name: $('secret-name').value.trim(), kind: $('secret-kind').value,
                value: $('secret-value').value, description: $('secret-desc').value.trim(), expiresOn,
            };
            await api('/admin/secrets', { method: 'POST', body: JSON.stringify(body) });
            toast(`Added ${body.name}`);
        }
        $('secret-value').value = '';
        hideModal('secret-overlay');
        loadSecretsPage();
    } catch (err) { setErr('secret-error', msgOf(err)); }
});

// ── history ───────────────────────────────────────────────────────────────────
const EVENT_LABEL = {
    CREATED: 'Created', UPDATED: 'Changed', DELETED: 'Deleted', REVEALED: 'Value shown', COPIED: 'Value copied',
    REVEAL_DENIED: 'Reveal refused', READ_BY_RUN: 'Read by a run', READ_DENIED: 'Run refused',
    VARIABLES_CHANGED: 'Variables changed',
};

function eventDetail(ev) {
    const d = ev.detail || {};
    if (d.fields) return 'Changed: ' + d.fields.join(', ');
    if (d.reason) return d.reason;
    if (d.group) return 'Group ' + d.group;
    return '';
}

async function openHistory(name) {
    $('history-title').textContent = `History of ${name}`;
    $('history-body').innerHTML = '<tr class="empty-row"><td colspan="4">Loading…</td></tr>';
    showModal('history-overlay');
    try {
        const out = await api(`/admin/secrets/${encodeURIComponent(name)}/events`);
        $('history-body').innerHTML = out.events.length
            ? out.events.map(ev => `<tr><td>${esc(fmtDate(ev.at))}</td><td>${esc(EVENT_LABEL[ev.kind] || ev.kind)}</td>` +
                `<td>${esc(ev.actor)}</td><td>${esc(eventDetail(ev))}</td></tr>`).join('')
            : '<tr class="empty-row"><td colspan="4">No history</td></tr>';
    } catch (err) {
        $('history-body').innerHTML = `<tr class="empty-row"><td colspan="4">${esc(msgOf(err))}</td></tr>`;
    }
}
$('history-dismiss').addEventListener('click', () => hideModal('history-overlay'));

// ── import ────────────────────────────────────────────────────────────────────
let importRows = [];
const IMPORT_COLUMNS = ['name', 'value', 'kind', 'description', 'expireson'];

// A CSV with quoted fields, doubled quotes and line breaks inside quotes.
function parseCsv(text) {
    const rows = [];
    let row = [], field = '', quoted = false;
    text = text.replace(/^﻿/, '');
    for (let i = 0; i < text.length; i++) {
        const ch = text[i];
        if (quoted) {
            if (ch === '"' && text[i + 1] === '"') { field += '"'; i++; }
            else if (ch === '"') quoted = false;
            else field += ch;
        } else if (ch === '"') quoted = true;
        else if (ch === ',') { row.push(field); field = ''; }
        else if (ch === '\n' || ch === '\r') {
            if (ch === '\r' && text[i + 1] === '\n') i++;
            row.push(field); field = '';
            if (row.some(c => c !== '')) rows.push(row);
            row = [];
        } else field += ch;
    }
    row.push(field);
    if (row.some(c => c !== '')) rows.push(row);
    return rows;
}

function rowsFromCsv(text) {
    const table = parseCsv(text);
    if (!table.length) throw new Error('The file is empty');
    const head = table[0].map(h => h.trim().toLowerCase());
    for (const need of ['name', 'value']) {
        if (!head.includes(need)) throw new Error(`The file has no "${need}" column`);
    }
    const idx = Object.fromEntries(IMPORT_COLUMNS.map(c => [c, head.indexOf(c)]));
    const get = (r, c) => idx[c] >= 0 ? (r[idx[c]] || '').trim() : '';
    return table.slice(1).map(r => ({
        name: get(r, 'name'), value: (idx.value >= 0 ? r[idx.value] || '' : ''), kind: get(r, 'kind') || 'text',
        description: get(r, 'description'), expiresOn: get(r, 'expireson'),
    }));
}

function renderImportPreview(errors) {
    const byRow = {};
    (errors || []).forEach(e => { byRow[e.row] = e.message; });
    $('import-preview').style.display = importRows.length ? '' : 'none';
    $('import-body').innerHTML = importRows.map((r, i) => {
        const err = byRow[i + 1];
        return `<tr${err ? ' class="row-error"' : ''}><td>${i + 1}</td><td>${esc(r.name)}</td><td>${esc(r.kind)}</td>` +
               `<td>${r.value ? STARS : '<span class="exp-expired">missing</span>'}</td><td>${esc(r.expiresOn) || '<span class="muted">No expiry</span>'}</td>` +
               `<td>${err ? esc(err) : ''}</td></tr>`;
    }).join('');
}

function openImport() {
    importRows = [];
    $('import-file').value = '';
    $('import-replace').checked = false;
    $('import-body').innerHTML = '';
    $('import-preview').style.display = 'none';
    setErr('import-error', '');
    $('import-ok').textContent = '';
    $('import-check').disabled = true;
    $('import-run').disabled = true;
    showModal('import-overlay');
}

async function importCall(dryRun) {
    return api('/admin/secrets/import', {
        method: 'POST', keepOnUnauthorized: false,
        body: JSON.stringify({ rows: importRows, replaceExisting: $('import-replace').checked, dryRun }),
    });
}

$('btn-secret-import').addEventListener('click', openImport);
$('import-dismiss').addEventListener('click', () => { importRows = []; hideModal('import-overlay'); });
// Choosing the same file again (after fixing it) must read it again, so the box is emptied first.
$('import-file').addEventListener('click', e => { e.target.value = ''; });
$('import-file').addEventListener('change', async e => {
    setErr('import-error', '');
    $('import-ok').textContent = '';
    const f = e.target.files[0];
    if (!f) return;
    try {
        importRows = rowsFromCsv(await f.text());
        if (!importRows.length) throw new Error('The file has a header but no secrets');
        renderImportPreview();
        $('import-check').disabled = false;
        $('import-run').disabled = false;
    } catch (err) {
        importRows = [];
        renderImportPreview();
        $('import-check').disabled = true;
        $('import-run').disabled = true;
        setErr('import-error', msgOf(err));
    }
});

$('import-check').addEventListener('click', async () => {
    setErr('import-error', '');
    $('import-ok').textContent = '';
    try {
        const out = await importCall(true);
        renderImportPreview();
        $('import-ok').textContent = `All ${importRows.length} rows are valid: ${out.wouldCreate} would be added, ${out.wouldReplace} replaced.`;
    } catch (err) {
        renderImportPreview(err.body && err.body.errors);
        setErr('import-error', msgOf(err));
    }
});

$('import-run').addEventListener('click', async () => {
    setErr('import-error', '');
    $('import-ok').textContent = '';
    try {
        const out = await importCall(false);
        $('import-ok').textContent = `Imported: ${out.created} added, ${out.replaced} replaced. The file still holds the values in clear: delete it now.`;
        importRows = [];
        $('import-run').disabled = true;
        $('import-check').disabled = true;
        $('import-file').value = '';
        loadSecretsPage();
    } catch (err) {
        renderImportPreview(err.body && err.body.errors);
        setErr('import-error', msgOf(err));
    }
});

// ══ Groups ═══════════════════════════════════════════════════════════════════
let groups = [];
let editingGroup = null;      // the group open on the group page; null for a new one
let scriptChoices = [];
let secretNames = [];

// Script picker for a group: one type-ahead box per script, + adds a row, - removes it.
async function loadScriptChoices() {
    try {
        const d = await api('/artifacts');
        const byId = new Map();
        for (const a of d.artifacts || []) {
            if (!byId.has(a.artifactId)) byId.set(a.artifactId, { id: a.artifactId, name: a.name || a.artifactId, versions: [] });
            byId.get(a.artifactId).versions.push(a.version);
        }
        scriptChoices = [...byId.values()].sort((a, b) => a.name.localeCompare(b.name));
    } catch (_) { scriptChoices = []; }
}

function scriptLabel(c) {
    return `${c.name} (${c.id}) — version ${c.versions.join(', ')}`;
}

function scriptRow(selected) {
    const row = document.createElement('div');
    row.className = 'script-row';
    row.innerHTML = '<input type="text" list="group-script-options" spellcheck="false" autocomplete="off" placeholder="Type to search, or pick a script">' +
        '<button type="button" class="btn btn-secondary btn-sm script-minus" title="Remove this script">−</button>' +
        '<button type="button" class="btn btn-secondary btn-sm script-plus" title="Add another script">+</button>';
    row.querySelector('input').value = selected || '';
    return row;
}

function fillScriptOptions() {
    let dl = document.getElementById('group-script-options');
    if (!dl) { dl = document.createElement('datalist'); dl.id = 'group-script-options'; document.body.appendChild(dl); }
    dl.innerHTML = '<option value="*" label="Every script"></option>' +
        scriptChoices.map(c => `<option value="${esc(c.id)}" label="${esc(scriptLabel(c))}"></option>`).join('');
}

// Shows + only on the last row and − on every row when there is more than one.
function refreshRowButtons(boxId, rowClass) {
    const rows = [...document.querySelectorAll(`#${boxId} .${rowClass}`)];
    rows.forEach((r, i) => {
        r.querySelector('.row-minus, .script-minus').style.visibility = rows.length > 1 ? 'visible' : 'hidden';
        r.querySelector('.row-plus, .script-plus').style.visibility = i === rows.length - 1 ? 'visible' : 'hidden';
    });
}

function setScriptRows(list) {
    const box = $('group-scripts-list');
    box.innerHTML = '';
    (list.length ? list : ['']).forEach(s => box.appendChild(scriptRow(s)));
    refreshRowButtons('group-scripts-list', 'script-row');
}

$('group-scripts-list').addEventListener('click', e => {
    const box = $('group-scripts-list');
    if (e.target.closest('.script-plus')) box.appendChild(scriptRow(''));
    else if (e.target.closest('.script-minus')) {
        e.target.closest('.script-row').remove();
        if (!box.children.length) box.appendChild(scriptRow(''));
    } else return;
    refreshRowButtons('group-scripts-list', 'script-row');
});

// Environment variables of a group: NAME, then a plain value or a secret from the Secrets tab.
let currentGroupNo = null;   // the group's number; null until a new group is saved
const groupPrefix = () => (currentGroupNo == null ? 'GP…_' : `GP${currentGroupNo}_`);

function setVarPrefix() {
    document.querySelectorAll('#group-vars-list .var-prefix').forEach(el => { el.textContent = groupPrefix(); });
}

function varRow(v) {
    const row = document.createElement('div');
    row.className = 'var-row';
    const isSecret = !!(v && v.secret);
    row.innerHTML = `<span class="var-prefix" title="Every variable of this group starts with this">${esc(groupPrefix())}</span>` +
        '<input type="text" class="var-name" spellcheck="false" autocomplete="off" placeholder="NAME">' +
        '<select class="var-mode"><option value="value">Value</option><option value="secret">Secret</option></select>' +
        '<span class="var-val"></span>' +
        '<button type="button" class="btn btn-secondary btn-sm row-minus script-minus" title="Remove this variable">−</button>' +
        '<button type="button" class="btn btn-secondary btn-sm row-plus script-plus" title="Add another variable">+</button>';
    row.querySelector('.var-name').value = v ? v.name : '';
    row.querySelector('.var-mode').value = isSecret ? 'secret' : 'value';
    fillVarValue(row, isSecret ? v.secret : (v && v.value != null ? v.value : ''));
    return row;
}

// Puts a text box or a secrets drop-down in the value slot, according to the mode.
function fillVarValue(row, current) {
    const slot = row.querySelector('.var-val');
    const mode = row.querySelector('.var-mode').value;
    if (mode === 'secret') {
        const names = secretNames.includes(current) || !current ? secretNames : [current, ...secretNames];
        slot.innerHTML = '<select class="var-input"><option value="">Choose a secret…</option>' +
            names.map(n => `<option value="${esc(n)}">${esc(n)}${secretNames.includes(n) ? '' : ' (not found)'}</option>`).join('') + '</select>';
        slot.querySelector('select').value = current || '';
    } else {
        slot.innerHTML = '<input type="text" class="var-input" autocomplete="off" placeholder="the value">';
        slot.querySelector('input').value = current || '';
    }
}

function setVarRows(list) {
    const box = $('group-vars-list');
    box.innerHTML = '';
    (list.length ? list : [null]).forEach(v => box.appendChild(varRow(v)));
    refreshRowButtons('group-vars-list', 'var-row');
}

$('group-vars-list').addEventListener('click', e => {
    const box = $('group-vars-list');
    if (e.target.closest('.row-plus')) box.appendChild(varRow(null));
    else if (e.target.closest('.row-minus')) {
        e.target.closest('.var-row').remove();
        if (!box.children.length) box.appendChild(varRow(null));
    } else return;
    refreshRowButtons('group-vars-list', 'var-row');
});
$('group-vars-list').addEventListener('change', e => {
    if (e.target.classList.contains('var-mode')) fillVarValue(e.target.closest('.var-row'), '');
});

// The variables typed in, as the API wants them; an untouched empty row is left out.
function collectVariables() {
    const out = [];
    const seen = new Set();
    for (const row of document.querySelectorAll('#group-vars-list .var-row')) {
        let name = row.querySelector('.var-name').value.trim();
        name = name.replace(/^GP\d+_/i, '');   // a pasted full name: the prefix is already shown
        const mode = row.querySelector('.var-mode').value;
        const val = row.querySelector('.var-input').value;
        if (!name && !val.trim()) continue;
        if (!name) throw new Error('A variable has a value but no name');
        if (seen.has(name)) throw new Error(`The variable ${groupPrefix()}${name} is listed twice`);
        seen.add(name);
        if (mode === 'secret') {
            if (!val) throw new Error(`Choose a secret for ${groupPrefix()}${name}`);
            out.push({ name, secret: val });
        } else {
            out.push({ name, value: val });
        }
    }
    return out;
}

function renderGroups() {
    const body = $('group-body');
    if (!groups.length) { body.innerHTML = '<tr class="empty-row"><td colspan="5">No groups yet</td></tr>'; return; }
    body.innerHTML = groups.map(g => {
        const scripts = g.scripts.length ? g.scripts.map(s => `<code>${esc(s)}</code>`).join(' ') : '<span class="muted">none: no script can read it</span>';
        return `<tr class="group-row" data-name="${esc(g.name)}" title="Open ${esc(g.name)}"><td><code>${g.number != null ? 'GP' + esc(g.number) : ''}</code></td><td><strong>${esc(g.name)}</strong></td>` +
               `<td>${esc(g.description)}</td><td>${g.variableCount}</td><td>${scripts}</td></tr>`;
    }).join('');
}

async function loadGroupsPage() {
    if ($('group-page').style.display !== 'none') return;   // a group is open: not redrawn
    try {
        groups = (await api('/admin/credential-groups')).groups;
        renderGroups();
    } catch (err) { toast(msgOf(err), true); }
}

function showGroupsList() {
    $('group-page').style.display = 'none';
    $('groups-list').style.display = '';
    editingGroup = null;
    loadGroupsPage();
}

async function openGroupPage(name) {
    setErr('group-error', '');
    $('group-ok').textContent = '';
    let g = { name: '', description: '', scripts: [], variables: [] };
    if (name) {
        try { g = await api(`/admin/credential-groups/${encodeURIComponent(name)}`); }
        catch (err) { toast(msgOf(err), true); return; }
    }
    editingGroup = name ? g : null;
    currentGroupNo = name && g.number != null ? g.number : null;
    await Promise.all([
        loadScriptChoices(),
        api('/admin/secrets').then(d => { secretNames = (d.secrets || []).map(s => s.name).sort(); }).catch(() => { secretNames = []; }),
    ]);
    fillScriptOptions();
    $('group-page-title').textContent = name ? `Group ${g.name}${currentGroupNo != null ? ' (GP' + currentGroupNo + ')' : ''}` : 'New group';
    $('group-number-note').textContent = currentGroupNo != null
        ? `Group number ${currentGroupNo}. Scripts read these variables as \${env:${groupPrefix()}NAME}.`
        : 'The group number is assigned when you save. Every variable then starts with GP<number>_.';
    $('group-name').value = g.name;
    $('group-name').disabled = !!name;
    $('group-desc').value = g.description || '';
    setScriptRows(g.scripts || []);
    setVarRows(g.variables || []);
    const canGroup = can('credentials.groups');
    const canVars = can('settings.edit');
    $('group-name').disabled = !!name || !canGroup;
    $('group-desc').disabled = !canGroup;
    document.querySelectorAll('#group-scripts-list input, #group-scripts-list button').forEach(el => { el.disabled = !canGroup; });
    document.querySelectorAll('#group-vars-list input, #group-vars-list select, #group-vars-list button').forEach(el => { el.disabled = !canVars; });
    $('group-save').hidden = !(canGroup || canVars);
    $('btn-group-delete').style.display = name ? '' : 'none';
    $('groups-list').style.display = 'none';
    $('group-page').style.display = '';
    (name ? $('group-desc') : $('group-name')).focus();
}

$('btn-group-add').addEventListener('click', () => openGroupPage(null));
$('btn-group-refresh').addEventListener('click', loadGroupsPage);
$('btn-group-back').addEventListener('click', showGroupsList);
$('group-dismiss').addEventListener('click', showGroupsList);
$('group-body').addEventListener('click', e => {
    const row = e.target.closest('.group-row');
    if (row) openGroupPage(row.dataset.name);
});

$('group-save').addEventListener('click', async () => {
    setErr('group-error', '');
    $('group-ok').textContent = '';
    const canGroup = can('credentials.groups');
    const canVars = can('settings.edit');
    const scripts = [...new Set([...document.querySelectorAll('#group-scripts-list input')].map(x => x.value.trim()).filter(Boolean))];
    let variables;
    try { variables = canVars ? collectVariables() : null; }
    catch (err) { setErr('group-error', msgOf(err)); return; }
    const description = $('group-desc').value.trim();
    try {
        let name;
        if (editingGroup) {
            name = editingGroup.name;
            if (canGroup) {
                await api(`/admin/credential-groups/${encodeURIComponent(name)}`, {
                    method: 'PUT', body: JSON.stringify({ description, scripts }),
                });
            }
        } else {
            name = $('group-name').value.trim();
            const made = await api('/admin/credential-groups', { method: 'POST', body: JSON.stringify({ name, description, scripts }) });
            editingGroup = { name, description, scripts, variables: [], number: made && made.number };
            if (made && made.number != null) {
                currentGroupNo = made.number;
                setVarPrefix();
                $('group-number-note').textContent = `Group number ${currentGroupNo}. Scripts read these variables as \${env:${groupPrefix()}NAME}.`;
            }
            $('group-name').disabled = true;
            $('group-page-title').textContent = `Group ${name}${currentGroupNo != null ? ' (GP' + currentGroupNo + ')' : ''}`;
            $('btn-group-delete').style.display = '';
        }
        if (canVars) {
            await api(`/admin/credential-groups/${encodeURIComponent(name)}/variables`, {
                method: 'PUT', body: JSON.stringify({ variables }),
            });
        }
        $('group-ok').textContent = 'Saved.';
        toast('Group saved');
    } catch (err) { setErr('group-error', msgOf(err)); }
});

$('btn-group-delete').addEventListener('click', () => {
    if (!editingGroup) return;
    const name = editingGroup.name;
    confirmDialog('Delete group', `Delete the group ${name} and its variables? Scripts that read them will fail.`, 'Delete', async () => {
        await api(`/admin/credential-groups/${encodeURIComponent(name)}`, { method: 'DELETE' });
        toast(`Deleted group ${name}`);
        showGroupsList();
    });
});

// ══ Users ════════════════════════════════════════════════════════════════════
let users = [];
let roles = [];
let editingUser = null;

async function loadUsers() {
    try {
        const out = await api('/admin/users');
        users = out.users;
        roles = out.roles;
        $('user-body').innerHTML = users.map(u => {
            const groupsText = u.allowedGroups === null ? '<span class="muted">every group</span>' : (u.allowedGroups.map(g => `<code>${esc(g)}</code>`).join(' ') || '<span class="muted">none</span>');
            const status = u.disabled ? '<span class="badge b-OFFLINE">Disabled</span>' : '<span class="badge b-AVAILABLE">Active</span>';
            return `<tr><td><strong>${esc(u.username)}</strong></td><td>${esc(u.role)}</td><td>${groupsText}</td><td>${status}</td>` +
                   `<td>${u.lastSignInAt ? esc(fmtDate(u.lastSignInAt)) : '<span class="muted">never</span>'}</td>` +
                   `<td class="actions"><button class="btn btn-secondary btn-sm" data-act="edit" data-name="${esc(u.username)}">Edit</button></td></tr>`;
        }).join('');
    } catch (err) { toast(msgOf(err), true); }
}

function openUserModal(u) {
    editingUser = u || null;
    setErr('user-error', '');
    $('user-modal-title').textContent = u ? `Edit ${u.username}` : 'Add a user';
    $('user-name').value = u ? u.username : '';
    $('user-name').disabled = !!u;
    $('user-role').innerHTML = roles.map(r => `<option value="${esc(r)}">${esc(r)}</option>`).join('');
    $('user-role').value = u ? u.role : 'Viewer';
    $('user-groups').value = u && u.allowedGroups ? u.allowedGroups.join(', ') : '';
    $('user-disabled').checked = !!(u && u.disabled);
    $('user-disabled-row').style.display = u ? '' : 'none';
    $('user-password').value = '';
    $('user-password').type = 'password';
    $('user-password-label').textContent = u ? 'New password' : 'Password';
    $('user-password-hint').textContent = u ? 'Leave empty to keep the password. A new one signs the user out everywhere.'
        : 'At least 8 characters with a capital letter, a small letter, a digit and a symbol.';
    showModal('user-overlay');
    (u ? $('user-role') : $('user-name')).focus();
}

$('btn-user-add').addEventListener('click', async () => { if (!roles.length) await loadUsers(); openUserModal(null); });
$('btn-user-refresh').addEventListener('click', loadUsers);
$('user-dismiss').addEventListener('click', () => { $('user-password').value = ''; hideModal('user-overlay'); });
$('user-body').addEventListener('click', e => {
    const btn = e.target.closest('[data-act="edit"]');
    if (btn) openUserModal(users.find(u => u.username === btn.dataset.name));
});

$('user-save').addEventListener('click', async () => {
    setErr('user-error', '');
    // An empty list means every group, which the API reads as null.
    const groupList = $('user-groups').value.trim() ? csvList($('user-groups').value) : null;
    try {
        if (editingUser) {
            const body = { role: $('user-role').value, allowedGroups: groupList, disabled: $('user-disabled').checked };
            if ($('user-password').value) body.newPassword = $('user-password').value;
            await api(`/admin/users/${encodeURIComponent(editingUser.username)}`, { method: 'PUT', body: JSON.stringify(body) });
        } else {
            const body = { username: $('user-name').value.trim(), role: $('user-role').value, password: $('user-password').value };
            if (groupList) body.allowedGroups = groupList;
            await api('/admin/users', { method: 'POST', body: JSON.stringify(body) });
        }
        $('user-password').value = '';
        hideModal('user-overlay');
        toast('User saved');
        loadUsers();
    } catch (err) { setErr('user-error', msgOf(err)); }
});

// ══ Change password and sign out ═════════════════════════════════════════════
$('btn-change-password').addEventListener('click', () => {
    ['pw-current', 'pw-new', 'pw-again'].forEach(id => { $(id).value = ''; $(id).type = 'password'; });
    setErr('pw-error', '');
    $('pw-ok').textContent = '';
    switchTab('password');
    $('pw-current').focus();
});

$('pw-form').addEventListener('submit', async e => {
    e.preventDefault();
    setErr('pw-error', '');
    $('pw-ok').textContent = '';
    if ($('pw-new').value !== $('pw-again').value) { setErr('pw-error', 'The two new passwords are not the same'); return; }
    $('pw-save').disabled = true;
    try {
        const res = await fetch('/ui-auth/password', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ currentPassword: $('pw-current').value, newPassword: $('pw-new').value }),
        });
        const body = await res.json().catch(() => ({}));
        if (res.status === 401) { window.location.href = '/login.html'; return; }
        if (!res.ok) { setErr('pw-error', body.message || 'Could not change the password'); return; }
        ['pw-current', 'pw-new', 'pw-again'].forEach(id => { $(id).value = ''; });
        $('pw-ok').textContent = 'Password changed. Your other sessions were signed out.';
    } catch {
        setErr('pw-error', 'Cannot reach the server');
    } finally {
        $('pw-save').disabled = false;
    }
});

$('btn-signout').addEventListener('click', async () => {
    try { await fetch('/ui-auth/logout', { method: 'POST' }); } catch { /* leaving anyway */ }
    window.location.href = '/login.html';
});

// ── Boot ──────────────────────────────────────────────────────────────────────
loadMe().catch(() => { /* api() already sent a signed-out user to the login page */ });
