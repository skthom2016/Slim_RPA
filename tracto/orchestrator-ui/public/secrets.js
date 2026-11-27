'use strict';

// Credentials, groups, settings, users and the password page. Loaded after app.js, which provides
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

// ══ Credentials ══════════════════════════════════════════════════════════════
let groups = [];
let creds = [];
let expiryFilter = false;     // show only credentials that expire soon or have expired
const revealed = new Map();   // credential name → timer that hides its password again
const REVEAL_MS = 30000;

function maskedPassword(name) {
    const cell = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] .pw`);
    if (cell) { cell.textContent = '●●●●●●●●'; cell.classList.remove('shown'); }
    const eye = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] [data-act="reveal"]`);
    if (eye) eye.title = 'Show the password';
}

function hidePassword(name) {
    clearTimeout(revealed.get(name));
    revealed.delete(name);
    maskedPassword(name);
}

function hideAllPasswords() {
    [...revealed.keys()].forEach(hidePassword);
}

// The password disappears when the page is left, as well as after 30 seconds.
window.addEventListener('blur', hideAllPasswords);
document.addEventListener('visibilitychange', () => { if (document.hidden) hideAllPasswords(); });

function expiryCell(c) {
    switch (c.expiryStatus) {
        case 'none':    return '<span class="muted">No expiry</span>';
        case 'soon':    return `<span class="exp-soon" title="Expires soon">⚠ ${esc(c.expiresOn)}</span>`;
        case 'expired': return `<span class="exp-expired" title="Expired">⛔ ${esc(c.expiresOn)} · Expired</span>`;
        default:        return esc(c.expiresOn);
    }
}

function renderCredentials() {
    const body = $('cred-body');
    const rows = creds.filter(c => !expiryFilter || c.expiryStatus === 'soon' || c.expiryStatus === 'expired');
    if (!rows.length) {
        body.innerHTML = `<tr class="empty-row"><td colspan="7">${creds.length ? 'Nothing matches' : 'No credentials yet'}</td></tr>`;
        return;
    }
    const reveal = can('credentials.reveal');
    const edit = can('credentials.edit');
    body.innerHTML = rows.map(c => {
        const site = c.siteUrl ? `<span title="${esc(c.siteUrl)}">${esc(c.siteUrl)}</span>` : '<span class="muted">—</span>';
        const pw = `<span class="pw-cell" data-name="${esc(c.name)}"><span class="pw">●●●●●●●●</span>` +
            (reveal ? ` <button class="icon-btn" data-act="reveal" data-name="${esc(c.name)}" title="Show the password" aria-label="Show the password">👁</button>` +
                      ` <button class="icon-btn" data-act="copy" data-name="${esc(c.name)}" title="Copy the password" aria-label="Copy the password">⧉</button>` : '') +
            (c.hasTotp ? ' <span class="badge b-default" title="Has a one-time-code key">code</span>' : '') + '</span>';
        const actions = `<button class="btn btn-secondary btn-sm" data-act="history" data-name="${esc(c.name)}">History</button>` +
            (edit ? ` <button class="btn btn-secondary btn-sm" data-act="edit" data-name="${esc(c.name)}" title="Edit">✎</button>` +
                    ` <button class="btn btn-outline-danger btn-sm" data-act="delete" data-name="${esc(c.name)}" title="Delete">🗑</button>` : '');
        return `<tr><td><strong>${esc(c.name)}</strong></td><td>${site}</td><td>${esc(c.username)}</td><td>${pw}</td>` +
               `<td>${esc(c.group)}</td><td>${expiryCell(c)}</td><td class="actions">${actions}</td></tr>`;
    }).join('');
}

function renderSummary(s) {
    const parts = [];
    if (s.soon)    parts.push(`⚠ ${s.soon} expiring soon`);
    if (s.expired) parts.push(`⛔ ${s.expired} expired`);
    const el = $('cred-summary');
    el.innerHTML = parts.length
        ? `<button class="link-btn${expiryFilter ? ' active' : ''}" id="cred-summary-btn" title="Show only these">${esc(parts.join(' · '))}${expiryFilter ? ' (showing only these)' : ''}</button>`
        : '';
    const btn = $('cred-summary-btn');
    if (btn) btn.addEventListener('click', () => { expiryFilter = !expiryFilter; renderSummary(s); renderCredentials(); });
    else expiryFilter = false;
}

function fillGroupSelects() {
    const filter = $('cred-group-filter');
    const keep = filter.value;
    filter.innerHTML = '<option value="">All groups</option>' + groups.map(g => `<option value="${esc(g.name)}">${esc(g.name)}</option>`).join('');
    filter.value = groups.some(g => g.name === keep) ? keep : '';
}

async function loadGroups() {
    groups = (await api('/admin/credential-groups')).groups;
    fillGroupSelects();
}

async function loadCredentialsPage() {
    hideAllPasswords();
    try {
        await loadGroups();
        const q = new URLSearchParams();
        if ($('cred-group-filter').value) q.set('group', $('cred-group-filter').value);
        if ($('cred-search').value.trim()) q.set('q', $('cred-search').value.trim());
        const out = await api('/admin/credentials' + (q.toString() ? '?' + q : ''));
        $('cred-locked').style.display = 'none';
        $('sub-creds').style.opacity = '';
        creds = out.credentials;
        renderSummary(out.summary);
        renderCredentials();
        renderGroups();
    } catch (err) {
        if (err.body && err.body.error === 'CREDENTIAL_STORE_LOCKED') {
            $('cred-locked').style.display = '';
            $('cred-body').innerHTML = '<tr class="empty-row"><td colspan="7">The credential store is locked</td></tr>';
            $('cred-summary').innerHTML = '';
            $('btn-cred-add').hidden = true;
            $('btn-cred-import').hidden = true;
        } else {
            toast(msgOf(err), true);
        }
    }
}

// ── reveal, copy and the row buttons ──────────────────────────────────────────
async function revealPassword(name, purpose) {
    const out = await api(`/admin/credentials/${encodeURIComponent(name)}/reveal`, {
        method: 'POST', body: JSON.stringify({ purpose }),
    });
    return out.password;
}

$('cred-body').addEventListener('click', async e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const name = btn.dataset.name;
    try {
        switch (btn.dataset.act) {
            case 'reveal': {
                if (revealed.has(name)) { hidePassword(name); return; }
                const pw = await revealPassword(name, 'reveal');
                const cell = document.querySelector(`.pw-cell[data-name="${CSS.escape(name)}"] .pw`);
                if (!cell) return;
                cell.textContent = pw;
                cell.classList.add('shown');
                btn.title = 'Hide the password';
                revealed.set(name, setTimeout(() => hidePassword(name), REVEAL_MS));
                break;
            }
            case 'copy': {
                const pw = await revealPassword(name, 'copy');
                await navigator.clipboard.writeText(pw);
                toast(`Password of ${name} copied`);
                break;
            }
            case 'history': openHistory(name); break;
            case 'edit':    openCredModal(creds.find(c => c.name === name)); break;
            case 'delete':
                confirmDialog('Delete credential', `Delete the credential ${name}? Its history is kept. This cannot be undone.`, 'Delete', async () => {
                    await api(`/admin/credentials/${encodeURIComponent(name)}`, { method: 'DELETE' });
                    toast(`Deleted ${name}`);
                    loadCredentialsPage();
                });
                break;
        }
    } catch (err) { toast(msgOf(err), true); }
});

$('cred-group-filter').addEventListener('change', loadCredentialsPage);
let searchTimer = null;
$('cred-search').addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(loadCredentialsPage, 250); });
$('btn-cred-refresh').addEventListener('click', loadCredentialsPage);

// ── add and edit ──────────────────────────────────────────────────────────────
let editingCred = null;

function setExpiryInputs(expiresOn) {
    $('cred-expiry-none').checked = !expiresOn;
    $('cred-expiry-date').checked = !!expiresOn;
    $('cred-expires').value = expiresOn || '';
    $('cred-expires').disabled = !expiresOn;
}
document.querySelectorAll('input[name="cred-expiry"]').forEach(r => r.addEventListener('change', () => {
    $('cred-expires').disabled = !$('cred-expiry-date').checked;
    if ($('cred-expiry-date').checked && !$('cred-expires').value) $('cred-expires').focus();
}));

function openCredModal(c) {
    editingCred = c || null;
    setErr('cred-error', '');
    $('cred-modal-title').textContent = c ? `Edit ${c.name}` : 'Add a credential';
    $('cred-name').value = c ? c.name : '';
    $('cred-name').disabled = !!c;
    $('cred-group').innerHTML = groups.map(g => `<option value="${esc(g.name)}">${esc(g.name)}</option>`).join('');
    if (c) $('cred-group').value = c.group;
    $('cred-username').value = c ? c.username : '';
    $('cred-password').value = '';
    $('cred-password').type = 'password';
    $('cred-password-hint').textContent = c ? 'Leave empty to keep the password as it is.' : '';
    $('cred-totp').value = '';
    $('cred-totp').type = 'password';
    $('cred-totp-hint').innerHTML = c && c.hasTotp
        ? 'A key is stored. Leave empty to keep it. <label class="check"><input type="checkbox" id="cred-totp-remove"> Remove the key</label>'
        : 'Only for sites that ask for a code from an authenticator app.';
    $('cred-site').value = c ? c.siteUrl : '';
    $('cred-notes').value = c ? c.notes : '';
    setExpiryInputs(c ? c.expiresOn : null);
    if (!groups.length) setErr('cred-error', 'Create a group first (Groups tab).');
    showModal('cred-overlay');
    (c ? $('cred-username') : $('cred-name')).focus();
}

$('btn-cred-add').addEventListener('click', () => openCredModal(null));
$('cred-dismiss').addEventListener('click', () => { $('cred-password').value = ''; hideModal('cred-overlay'); });

$('cred-save').addEventListener('click', async () => {
    setErr('cred-error', '');
    const expiresOn = $('cred-expiry-date').checked ? $('cred-expires').value : null;
    if ($('cred-expiry-date').checked && !expiresOn) { setErr('cred-error', 'Pick the expiry date, or choose No expiry.'); return; }
    try {
        if (editingCred) {
            const body = {
                group: $('cred-group').value, username: $('cred-username').value.trim(),
                siteUrl: $('cred-site').value.trim(), notes: $('cred-notes').value.trim(), expiresOn,
            };
            if ($('cred-password').value) body.password = $('cred-password').value;
            const remove = $('cred-totp-remove');
            if (remove && remove.checked) body.totpKey = '';
            else if ($('cred-totp').value.trim()) body.totpKey = $('cred-totp').value.trim();
            await api(`/admin/credentials/${encodeURIComponent(editingCred.name)}`, { method: 'PUT', body: JSON.stringify(body) });
            toast(`Saved ${editingCred.name}`);
        } else {
            const body = {
                name: $('cred-name').value.trim(), group: $('cred-group').value, username: $('cred-username').value.trim(),
                password: $('cred-password').value, siteUrl: $('cred-site').value.trim(), notes: $('cred-notes').value.trim(),
            };
            if ($('cred-totp').value.trim()) body.totpKey = $('cred-totp').value.trim();
            if (expiresOn) body.expiresOn = expiresOn;
            await api('/admin/credentials', { method: 'POST', body: JSON.stringify(body) });
            toast(`Added ${body.name}`);
        }
        $('cred-password').value = '';
        $('cred-totp').value = '';
        hideModal('cred-overlay');
        loadCredentialsPage();
    } catch (err) { setErr('cred-error', msgOf(err)); }
});

// ── history ───────────────────────────────────────────────────────────────────
const EVENT_LABEL = {
    CREATED: 'Created', UPDATED: 'Changed', DELETED: 'Deleted', REVEALED: 'Password shown', COPIED: 'Password copied',
    REVEAL_DENIED: 'Reveal refused', READ_BY_RUN: 'Read by a run', READ_DENIED: 'Run refused',
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
        const out = await api(`/admin/credentials/${encodeURIComponent(name)}/events`);
        $('history-body').innerHTML = out.events.length
            ? out.events.map(ev => `<tr><td>${esc(fmtDate(ev.at))}</td><td>${esc(EVENT_LABEL[ev.kind] || ev.kind)}</td>` +
                `<td>${esc(ev.actor)}</td><td>${esc(eventDetail(ev))}</td></tr>`).join('')
            : '<tr class="empty-row"><td colspan="4">No history</td></tr>';
    } catch (err) {
        $('history-body').innerHTML = `<tr class="empty-row"><td colspan="4">${esc(msgOf(err))}</td></tr>`;
    }
}
$('history-dismiss').addEventListener('click', () => hideModal('history-overlay'));

// Script picker for a group: one drop-down per script, + adds a row, - removes it.
let scriptChoices = [];

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

function refreshScriptButtons() {
    const rows = [...document.querySelectorAll('#group-scripts-list .script-row')];
    rows.forEach((r, i) => {
        r.querySelector('.script-minus').style.visibility = rows.length > 1 ? 'visible' : 'hidden';
        r.querySelector('.script-plus').style.visibility = i === rows.length - 1 ? 'visible' : 'hidden';
    });
}

function setScriptRows(list) {
    const box = $('group-scripts-list');
    box.innerHTML = '';
    (list.length ? list : ['']).forEach(s => box.appendChild(scriptRow(s)));
    refreshScriptButtons();
}

$('group-scripts-list').addEventListener('click', e => {
    const box = $('group-scripts-list');
    if (e.target.closest('.script-plus')) box.appendChild(scriptRow(''));
    else if (e.target.closest('.script-minus')) {
        e.target.closest('.script-row').remove();
        if (!box.children.length) box.appendChild(scriptRow(''));
    } else return;
    refreshScriptButtons();
});

// ── groups ────────────────────────────────────────────────────────────────────
let editingGroup = null;

function renderGroups() {
    const body = $('group-body');
    if (!groups.length) { body.innerHTML = '<tr class="empty-row"><td colspan="5">No groups yet</td></tr>'; return; }
    const edit = can('credentials.groups');
    body.innerHTML = groups.map(g => {
        const scripts = g.scripts.length ? g.scripts.map(s => `<code>${esc(s)}</code>`).join(' ') : '<span class="muted">none: no script can read it</span>';
        const actions = edit
            ? `<button class="btn btn-secondary btn-sm" data-act="edit" data-name="${esc(g.name)}" title="Edit">✎</button> ` +
              `<button class="btn btn-outline-danger btn-sm" data-act="delete" data-name="${esc(g.name)}" title="Delete">🗑</button>`
            : '';
        return `<tr><td><strong>${esc(g.name)}</strong></td><td>${esc(g.description)}</td><td>${g.credentialCount}</td><td>${scripts}</td><td class="actions">${actions}</td></tr>`;
    }).join('');
}

function openGroupModal(g) {
    editingGroup = g || null;
    setErr('group-error', '');
    $('group-modal-title').textContent = g ? `Edit group ${g.name}` : 'New group';
    $('group-name').value = g ? g.name : '';
    $('group-name').disabled = !!g;
    $('group-desc').value = g ? g.description : '';
    loadScriptChoices().then(() => { fillScriptOptions(); setScriptRows(g ? g.scripts : []); });
    showModal('group-overlay');
    (g ? $('group-desc') : $('group-name')).focus();
}

$('btn-group-add').addEventListener('click', () => openGroupModal(null));
$('btn-group-refresh').addEventListener('click', loadCredentialsPage);
$('group-dismiss').addEventListener('click', () => hideModal('group-overlay'));
$('group-save').addEventListener('click', async () => {
    setErr('group-error', '');
    const scripts = [...new Set([...document.querySelectorAll('#group-scripts-list input')].map(x => x.value.trim()).filter(Boolean))];
    try {
        if (editingGroup) {
            await api(`/admin/credential-groups/${encodeURIComponent(editingGroup.name)}`, {
                method: 'PUT', body: JSON.stringify({ description: $('group-desc').value.trim(), scripts }),
            });
        } else {
            await api('/admin/credential-groups', {
                method: 'POST', body: JSON.stringify({ name: $('group-name').value.trim(), description: $('group-desc').value.trim(), scripts }),
            });
        }
        hideModal('group-overlay');
        toast('Group saved');
        loadCredentialsPage();
    } catch (err) { setErr('group-error', msgOf(err)); }
});

$('group-body').addEventListener('click', e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const g = groups.find(x => x.name === btn.dataset.name);
    if (btn.dataset.act === 'edit') openGroupModal(g);
    if (btn.dataset.act === 'delete') {
        confirmDialog('Delete group', `Delete the group ${g.name}? A group that still holds credentials cannot be deleted.`, 'Delete', async () => {
            await api(`/admin/credential-groups/${encodeURIComponent(g.name)}`, { method: 'DELETE' });
            toast(`Deleted group ${g.name}`);
            loadCredentialsPage();
        });
    }
});

document.querySelectorAll('.subtab').forEach(t => t.addEventListener('click', () => {
    document.querySelectorAll('.subtab').forEach(x => x.classList.toggle('active', x === t));
    $('sub-creds').style.display = t.dataset.sub === 'creds' ? '' : 'none';
    $('sub-groups').style.display = t.dataset.sub === 'groups' ? '' : 'none';
}));

// ── import ────────────────────────────────────────────────────────────────────
let importRows = [];
const IMPORT_COLUMNS = ['name', 'group', 'username', 'password', 'siteurl', 'notes', 'expireson'];

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
    for (const need of ['name', 'group', 'username', 'password']) {
        if (!head.includes(need)) throw new Error(`The file has no "${need}" column`);
    }
    const idx = Object.fromEntries(IMPORT_COLUMNS.map(c => [c, head.indexOf(c)]));
    const get = (r, c) => idx[c] >= 0 ? (r[idx[c]] || '').trim() : '';
    return table.slice(1).map(r => ({
        name: get(r, 'name'), group: get(r, 'group'), username: get(r, 'username'), password: (idx.password >= 0 ? r[idx.password] || '' : ''),
        siteUrl: get(r, 'siteurl'), notes: get(r, 'notes'), expiresOn: get(r, 'expireson'),
    }));
}

function renderImportPreview(errors) {
    const byRow = {};
    (errors || []).forEach(e => { byRow[e.row] = e.message; });
    $('import-preview').style.display = importRows.length ? '' : 'none';
    $('import-body').innerHTML = importRows.map((r, i) => {
        const err = byRow[i + 1];
        return `<tr${err ? ' class="row-error"' : ''}><td>${i + 1}</td><td>${esc(r.name)}</td><td>${esc(r.group)}</td><td>${esc(r.username)}</td>` +
               `<td>${r.password ? '●●●●●●●●' : '<span class="exp-expired">missing</span>'}</td><td>${esc(r.expiresOn) || '<span class="muted">No expiry</span>'}</td>` +
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
    return api('/admin/credentials/import', {
        method: 'POST', keepOnUnauthorized: false,
        body: JSON.stringify({ rows: importRows, replaceExisting: $('import-replace').checked, dryRun }),
    });
}

$('btn-cred-import').addEventListener('click', openImport);
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
        if (!importRows.length) throw new Error('The file has a header but no credentials');
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
        $('import-ok').textContent = `Imported: ${out.created} added, ${out.replaced} replaced. The file still holds the passwords in clear: delete it now.`;
        importRows = [];
        $('import-run').disabled = true;
        $('import-check').disabled = true;
        $('import-file').value = '';
        loadCredentialsPage();
    } catch (err) {
        renderImportPreview(err.body && err.body.errors);
        setErr('import-error', msgOf(err));
    }
});

// ══ Settings ═════════════════════════════════════════════════════════════════
let settings = [];
let editingSetting = null;

async function loadSettings() {
    try {
        settings = (await api('/admin/settings')).settings;
        const edit = can('settings.edit');
        $('setting-body').innerHTML = settings.length ? settings.map(s => {
            const actions = edit
                ? `<button class="btn btn-secondary btn-sm" data-act="edit" data-name="${esc(s.name)}" title="Edit">✎</button> ` +
                  `<button class="btn btn-outline-danger btn-sm" data-act="delete" data-name="${esc(s.name)}" title="Delete">🗑</button>`
                : '';
            return `<tr><td><strong>${esc(s.name)}</strong></td><td>${esc(s.value)}</td><td>${esc(s.notes)}</td>` +
                   `<td>${esc(fmtDate(s.updatedAt))} · ${esc(s.updatedBy)}</td><td class="actions">${actions}</td></tr>`;
        }).join('') : '<tr class="empty-row"><td colspan="5">No settings yet</td></tr>';
    } catch (err) { toast(msgOf(err), true); }
}

function openSettingModal(s) {
    editingSetting = s || null;
    setErr('setting-error', '');
    $('setting-modal-title').textContent = s ? `Edit ${s.name}` : 'Add a setting';
    $('setting-name').value = s ? s.name : '';
    $('setting-name').disabled = !!s;
    $('setting-value').value = s ? s.value : '';
    $('setting-notes').value = s ? s.notes : '';
    showModal('setting-overlay');
    (s ? $('setting-value') : $('setting-name')).focus();
}

$('btn-setting-add').addEventListener('click', () => openSettingModal(null));
$('btn-setting-refresh').addEventListener('click', loadSettings);
$('setting-dismiss').addEventListener('click', () => hideModal('setting-overlay'));
$('setting-save').addEventListener('click', async () => {
    setErr('setting-error', '');
    const name = editingSetting ? editingSetting.name : $('setting-name').value.trim();
    try {
        await api(`/admin/settings/${encodeURIComponent(name)}`, {
            method: 'PUT', body: JSON.stringify({ value: $('setting-value').value, notes: $('setting-notes').value.trim() }),
        });
        hideModal('setting-overlay');
        toast(`Saved ${name}`);
        loadSettings();
    } catch (err) { setErr('setting-error', msgOf(err)); }
});
$('setting-body').addEventListener('click', e => {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const s = settings.find(x => x.name === btn.dataset.name);
    if (btn.dataset.act === 'edit') openSettingModal(s);
    if (btn.dataset.act === 'delete') {
        confirmDialog('Delete setting', `Delete the setting ${s.name}? Scripts that read it will fail.`, 'Delete', async () => {
            await api(`/admin/settings/${encodeURIComponent(s.name)}`, { method: 'DELETE' });
            toast(`Deleted ${s.name}`);
            loadSettings();
        });
    }
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
