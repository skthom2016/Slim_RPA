#!/usr/bin/env node
// Validates a Tracto Designer-format script offline. No services and no packages needed.
// Usage: node validate.js <script.json> [more.json ...]
// Exit code 0 = no errors (warnings allowed), 1 = errors, 2 = bad usage.

const fs = require('fs');
const path = require('path');

const ACTIONS = JSON.parse(fs.readFileSync(path.join(__dirname, 'actions.json'), 'utf8'));
const BUILTINS = new Set(['now.iso', 'now.local', 'now.date', 'now.time']);
const BOOL_PARAMS = new Set(['rightClick', 'attachIfRunning', 'overwrite']);
const INT_PARAMS = new Set(['ms', 'timeoutMs', 'delta']);
const SHEET_FIELDS = {
  '/batch': ['header', 'startRow', 'endRow', 'rows', 'scanned', 'count', 'endOfData', 'nextStartRow', 'text'],
  '/update': ['rows', 'cells'],
  '/apply': ['updated', 'rows', 'text'],
};

function checkHostDrift(report) {
  // Compare the action list with the handlers in the repo, when the script is run inside it.
  let dir = __dirname;
  for (let i = 0; i < 8; i++, dir = path.dirname(dir)) {
    const handlers = path.join(dir, 'tracto', 'desktop-runtime', 'src', 'Tracto.Desktop.Actions', 'Handlers');
    if (!fs.existsSync(handlers)) continue;
    const live = new Set();
    for (const f of fs.readdirSync(handlers).filter((n) => n.endsWith('.cs'))) {
      const m = /ActionName\s*=>\s*"([^"]+)"/.exec(fs.readFileSync(path.join(handlers, f), 'utf8'));
      if (m) live.add(m[1]);
    }
    live.delete('action'); // recorder fallback, not for hand-written scripts
    for (const a of live) if (!ACTIONS[a]) report.warn(`Host has action "${a}" that this validator does not know. Update scripts/actions.json and references/actions.md.`);
    for (const a of Object.keys(ACTIONS)) if (!live.has(a)) report.warn(`Validator lists "${a}" but no handler with that name exists in the repo.`);
    return;
  }
}

function validate(file) {
  const errors = [];
  const warnings = [];
  const report = { error: (m) => errors.push(m), warn: (m) => warnings.push(m) };

  let text;
  try { text = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''); }
  catch (e) { return { errors: [`cannot read file: ${e.message}`], warnings }; }

  let doc;
  try { doc = JSON.parse(text); }
  catch (e) { return { errors: [`not valid JSON: ${e.message}`], warnings }; }

  if (doc && doc.script && doc.schemaVersion !== undefined)
    report.error('This is the runtime envelope ({"schemaVersion","script"}). Write the flat Designer format; the Designer wraps it on publish.');
  else if (doc && doc.payload !== undefined)
    report.error('This is an upload body ({"schemaVersion","payload"}). Write the flat Designer format.');

  const top = ['id', 'name', 'version', 'targetRuntime', 'steps'];
  for (const k of top) if (doc[k] === undefined) report.error(`missing top-level field "${k}"`);
  for (const k of Object.keys(doc)) if (!top.includes(k)) report.error(`unknown top-level field "${k}" (allowed: ${top.join(', ')})`);

  if (typeof doc.id === 'string' && !/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(doc.id))
    report.error(`id "${doc.id}" must be lowercase letters, digits and hyphens, not starting or ending with a hyphen`);
  if (typeof doc.version === 'string' && !/^\d+\.\d+\.\d+$/.test(doc.version))
    report.error(`version "${doc.version}" must look like 1.0.0`);
  if (doc.targetRuntime !== undefined && doc.targetRuntime !== 'desktop')
    report.error('targetRuntime must be "desktop"');
  if (!Array.isArray(doc.steps) || doc.steps.length === 0) {
    report.error('"steps" must be a non-empty array');
    return { errors, warnings };
  }

  const defined = new Map(); // storeAs name -> { action }
  doc.steps.forEach((step, i) => {
    const where = `step ${i + 1}`;
    if (step.stepNumber !== i + 1) report.error(`${where}: stepNumber is ${JSON.stringify(step.stepNumber)}, expected ${i + 1} (1-based, in order, no gaps)`);
    for (const k of Object.keys(step)) if (!['stepNumber', 'action', 'description', 'parameters'].includes(k)) report.error(`${where}: unknown field "${k}"`);
    const spec = ACTIONS[step.action];
    if (!spec) { report.error(`${where}: unknown action "${step.action}". Known: ${Object.keys(ACTIONS).join(', ')}`); return; }
    if (!step.parameters || typeof step.parameters !== 'object') { report.error(`${where} (${step.action}): "parameters" object is required`); return; }

    const p = step.parameters;
    for (const [k, v] of Object.entries(p)) {
      if (typeof v !== 'string') report.error(`${where} (${step.action}): parameter "${k}" must be a string, got ${typeof v}. Write "${v}" in quotes.`);
    }
    for (const r of spec.required) if (p[r] === undefined || p[r] === '') report.error(`${where} (${step.action}): required parameter "${r}" is missing`);
    for (const k of Object.keys(p)) {
      if (k !== 'storeAs' && !spec.required.includes(k) && !spec.optional.includes(k))
        report.warn(`${where} (${step.action}): parameter "${k}" is not used by this action and is ignored`);
    }
    for (const [k, v] of Object.entries(p)) {
      if (typeof v !== 'string' || v.includes('${')) continue;
      if (BOOL_PARAMS.has(k) && !/^(true|false)$/i.test(v)) report.error(`${where}: "${k}" must be "true" or "false"`);
      if (INT_PARAMS.has(k) && !/^-?\d+$/.test(v)) report.error(`${where}: "${k}" must be a whole number in a string, got "${v}"`);
      if (k === 'coordinates' && !/^\d+,\d+$/.test(v)) report.error(`${where}: "coordinates" must be "x,y", got "${v}"`);
      if (k === 'offset' && !/^-?\d+,-?\d+$/.test(v)) report.error(`${where}: "offset" must be "x,y", got "${v}"`);
    }
    if (step.action === 'delay' && /^\d+$/.test(p.ms || '') && Number(p.ms) > 30000) report.warn(`${where}: delay is capped at 30000 ms`);
    // Backslashes lost when the file was written through a shell (C:\data\new.txt becomes "C:data" + newline + "ew.txt").
    for (const [k, v] of Object.entries(p)) {
      if (typeof v !== 'string') continue;
      if (/(^|[\s"'])[A-Za-z]:(?![\\/\s]|$)[^\s]/.test(v) && !/^[a-z][a-z0-9+.-]*:\/\//i.test(v))
        report.error(`${where}: "${k}" has a drive letter without a path separator ("${v.slice(0, 60)}"). Backslashes were probably lost when the file was written. Write the JSON with a file-writing tool, not a shell heredoc, and double each backslash.`);
      if (['filePath', 'executable', 'args', 'url', 'window', 'windowTitle'].includes(k) && /[\n\r\t]/.test(v))
        report.error(`${where}: "${k}" contains a line break or tab. A "\\n" or "\\t" inside a Windows path was probably written by mistake (use \\\\ for each backslash).`);
    }
    if (step.action === 'app.launch' && p.executable === 'notepad') report.warn(`${where}: prefer "notepad.exe"`);
    if (step.action === 'app.launch' && typeof p.executable === 'string' && !p.executable.includes('${')) {
      const exe = p.executable.toLowerCase().replace(/^.*[\\/]/, '');
      if (/^(cmd|powershell|pwsh|robocopy|xcopy|python|node|wsl)(\.exe)?$/.test(exe))
        report.warn(`${where}: app.launch of a console program (${exe}) does the work but then fails the step, because it waits for a window. Run it from the Run dialog instead (recipe: run a shell command).`);
      if (/\.(pdf|docx?|xlsx?|pptx?|txt|csv)$/.test(exe))
        report.warn(`${where}: app.launch with a document path depends on a default app and usually fails. Open the file with an explicit program from the Run dialog.`);
      if (/^(msedge|chrome|firefox)(\.exe)?$/.test(exe))
        report.warn(`${where}: app.launch of a browser that may already be running fails the step even though the window opens. Use the Run dialog.`);
    }
    if (step.action === 'app.launch' && typeof p.args === 'string' && p.args.includes('"'))
      report.warn(`${where}: args are split on spaces, so a quoted value with spaces is broken apart. Keep paths free of spaces.`);
    if (step.action === 'type' && typeof p.value === 'string' && /\{(ENTER|TAB|ESC|F\d+)\}/i.test(p.value))
      report.warn(`${where}: type sends braces as plain text. Use a key step for Enter, Tab and Escape.`);
    if (step.action === 'key' && /^alt\+f4$/i.test(p.key || '') && !p.window)
      report.warn(`${where}: Alt+F4 without "window" closes whatever window is active.`);
    if ((step.action === 'type' || step.action === 'key') && typeof p.window === 'string' && /^notepad$/i.test(p.window.trim()))
      report.warn(`${where}: window "Notepad" matches any Notepad window or tab; use a more specific title when others may be open.`);

    // variable references must come from an earlier step
    for (const [k, v] of Object.entries(p)) {
      if (typeof v !== 'string') continue;
      for (const m of v.matchAll(/\$\{([^}]+)\}/g)) {
        const ref = m[1];
        if (BUILTINS.has(ref)) continue;
        const [ns, ...rest] = ref.split('.');
        const field = rest.join('.');
        const src = defined.get(ns);
        if (!src) { report.error(`${where}: "\${${ref}}" is used but no earlier step has storeAs "${ns}". The text would be sent literally.`); continue; }
        const s = ACTIONS[src.action];
        if (!field) report.warn(`${where}: "\${${ref}}" has no field; use \${${ns}.<field>}`);
        else if (!s.outputs.includes(field) && !(src.fields && src.fields.includes(field)) && !s.flattensJson)
          report.warn(`${where}: step with storeAs "${ns}" (${src.action}) has no output "${field}". Outputs: ${s.outputs.join(', ') || 'none'}`);
        else if (s.flattensJson && !s.outputs.includes(field) && src.fields && !src.fields.includes(field))
          report.error(`${where}: "\${${ref}}" is not returned by ${src.sheetRoute}. Available: ${src.fields.join(', ')}`);
      }
    }

    // http.post / http.get checks, including the sheet service
    if ((step.action === 'http.post' || step.action === 'http.get') && typeof p.url === 'string' && !p.url.includes('${')) {
      if (!/^https?:\/\//i.test(p.url)) report.error(`${where}: url must start with http:// or https://`);
    }
    let sheetRoute = null;
    if (step.action === 'http.post' && typeof p.url === 'string') {
      const m = /^https?:\/\/(?:localhost|127\.0\.0\.1):5090(\/[a-z]*)/i.exec(p.url);
      if (m) sheetRoute = m[1];
    }
    if (step.action === 'http.post' && p.body && !p.body.includes('${')) {
      let body;
      try { body = JSON.parse(p.body); }
      catch (e) { if (!p.contentType || /json/i.test(p.contentType)) report.error(`${where}: "body" is not valid JSON (${e.message}). Inner quotes must be escaped as \\" and backslashes in paths doubled or written as /.`); }
      if (body && sheetRoute) checkSheetBody(sheetRoute, body, where, report);
      else if (sheetRoute && !body) { /* already reported */ }
    }
    if (sheetRoute && !SHEET_FIELDS[sheetRoute]) report.error(`${where}: sheet-service has no route "${sheetRoute}". Use /batch, /update or /apply.`);

    if (p.storeAs) {
      if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(p.storeAs)) report.error(`${where}: storeAs "${p.storeAs}" must be letters, digits and underscores`);
      if (defined.has(p.storeAs)) report.warn(`${where}: storeAs "${p.storeAs}" was already used by an earlier step and its outputs are overwritten`);
      defined.set(p.storeAs, { action: step.action, fields: sheetRoute ? SHEET_FIELDS[sheetRoute] : null, sheetRoute });
    }
  });

  checkHostDrift(report);
  return { errors, warnings };
}

function checkSheetBody(route, b, where, report) {
  const src = b.source;
  if (!src || typeof src !== 'object') { report.error(`${where}: sheet-service body needs "source": {"path", "sheet"}`); return; }
  if (!src.path) report.error(`${where}: source.path is required`);
  if (!src.sheet) report.error(`${where}: source.sheet (worksheet tab name) is required`);
  if (typeof src.path === 'string' && /^[A-Za-z]:\\/.test(src.path)) report.warn(`${where}: use forward slashes in source.path (C:/data/book.xlsx)`);
  for (const k of ['startRow', 'endRow', 'limit', 'headerRow']) {
    if (b[k] !== undefined && !Number.isInteger(b[k])) report.error(`${where}: "${k}" must be a number, not a string`);
  }
  if (b.startRow !== undefined && b.startRow < 2 && b.headerRow === undefined) report.warn(`${where}: startRow ${b.startRow} is the header row; data starts at row 2`);
  if (b.pending) {
    if (!b.pending.column) report.error(`${where}: pending.column is required`);
    if (!Array.isArray(b.pending.doneValues)) report.error(`${where}: pending.doneValues must be an array`);
  }
  if (route === '/apply') {
    if (!Number.isInteger(b.limit) || b.limit < 1) report.error(`${where}: /apply needs "limit" of at least 1`);
    if (!b.set || typeof b.set !== 'object' || Object.keys(b.set).length === 0) report.error(`${where}: /apply needs a non-empty "set" object`);
    else for (const [k, val] of Object.entries(b.set)) if (typeof val !== 'string') report.error(`${where}: set.${k} must be a string, got ${typeof val}`);
  }
  if (route === '/update' && !b.rows && !b.updates) report.error(`${where}: /update needs "rows" (or "updates")`);
  if (route === '/update' && Array.isArray(b.rows))
    b.rows.forEach((r, i) => { if (!Number.isInteger(r.row) || !r.values) report.error(`${where}: rows[${i}] needs {"row": <number>, "values": {...}}`); });
}

const files = process.argv.slice(2);
if (files.length === 0) { console.error('usage: node validate.js <script.json> [...]'); process.exit(2); }
let failed = false;
for (const f of files) {
  const { errors, warnings } = validate(f);
  console.log(`${f}: ${errors.length} error(s), ${warnings.length} warning(s)`);
  errors.forEach((e) => console.log(`  ERROR  ${e}`));
  warnings.forEach((w) => console.log(`  warn   ${w}`));
  if (errors.length) failed = true;
}
process.exit(failed ? 1 : 0);
