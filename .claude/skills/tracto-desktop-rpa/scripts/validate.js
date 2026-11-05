#!/usr/bin/env node
// Validates a Tracto Designer-format script offline. No services and no packages needed.
// Two shapes are accepted:
//   flat:      { id, name, version, targetRuntime, steps }
//   segmented: { id, name, version, params?, segments: [ { name?, runtime, steps } ] }
// Desktop steps get the full action checks. Browser and terminal steps get structural and
// variable-reference checks only; this skill does not own their action tables.
// Usage: node validate.js <script.json> [more.json ...]
// Exit code 0 = no errors (warnings allowed), 1 = errors, 2 = bad usage.

const fs = require('fs');
const path = require('path');

const ACTIONS = JSON.parse(fs.readFileSync(path.join(__dirname, 'actions.json'), 'utf8'));
const BUILTINS = new Set(['now.iso', 'now.local', 'now.date', 'now.time']);
const BOOL_PARAMS = new Set(['rightClick', 'attachIfRunning', 'overwrite']);
const INT_PARAMS = new Set(['ms', 'timeoutMs', 'delta', 'maxChars']);
const RUNTIMES = ['desktop', 'browser', 'terminal'];
const SEGMENT_FIELDS = ['name', 'runtime', 'mode', 'profile', 'steps'];
const BROWSER_MODES = ['headed', 'headless'];
const PROFILE_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
const STEP_FIELDS = ['stepNumber', 'action', 'description', 'parameters'];
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
  if (doc === null || typeof doc !== 'object' || Array.isArray(doc)) return { errors: ['the file must contain one JSON object'], warnings };

  if (doc.script && doc.schemaVersion !== undefined)
    report.error('This is the runtime envelope ({"schemaVersion","script"}). Write the flat Designer format; the Designer wraps it on publish.');
  else if (doc.payload !== undefined)
    report.error('This is an upload body ({"schemaVersion","payload"}). Write the flat Designer format.');

  const segmented = doc.segments !== undefined;
  const top = segmented ? ['id', 'name', 'version', 'params', 'segments'] : ['id', 'name', 'version', 'targetRuntime', 'steps'];
  const required = segmented ? ['id', 'name', 'version', 'segments'] : top;
  for (const k of required) if (doc[k] === undefined) report.error(`missing top-level field "${k}"`);
  for (const k of Object.keys(doc)) {
    if (top.includes(k)) continue;
    if (segmented && (k === 'targetRuntime' || k === 'steps'))
      report.error(`"${k}" is not allowed in a segmented script: each segment declares its own "runtime" and "steps"`);
    else report.error(`unknown top-level field "${k}" (allowed: ${top.join(', ')})`);
  }

  if (typeof doc.id === 'string' && !/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(doc.id))
    report.error(`id "${doc.id}" must be lowercase letters, digits and hyphens, not starting or ending with a hyphen`);
  if (typeof doc.version === 'string' && !/^\d+\.\d+\.\d+$/.test(doc.version))
    report.error(`version "${doc.version}" must look like 1.0.0`);

  if (segmented) validateSegmented(doc, report);
  else validateFlat(doc, report);

  checkHostDrift(report);
  return { errors, warnings };
}

// ── flat script ─────────────────────────────────────────────────────────────────────────────
function validateFlat(doc, report) {
  const rt = doc.targetRuntime;
  if (rt !== undefined && !RUNTIMES.includes(rt))
    report.error(`targetRuntime must be one of ${RUNTIMES.join(', ')}, got "${rt}"`);
  if (!Array.isArray(doc.steps) || doc.steps.length === 0) {
    report.error('"steps" must be a non-empty array');
    return;
  }
  const runtime = RUNTIMES.includes(rt) ? rt : 'desktop';
  if (runtime !== 'desktop')
    report.warn(`targetRuntime "${runtime}": this skill documents desktop actions only, so ${runtime} steps are checked for structure and variable references only.`);
  const ctx = { report, runtime, segmented: false, declared: new Set(), produced: new Map(), local: new Map(), warnedParams: new Set(), label: () => '' };
  checkSteps(doc.steps, ctx);
}

// ── segmented script ────────────────────────────────────────────────────────────────────────
// "mode" overrides the browser host's own headed/headless setting for one segment, and
// "profile" names a browser profile folder on the worker so sites stay signed in. Both mean
// nothing to a desktop or terminal segment. How long the browser stays open after the run is
// always the browser host's own PAUSE_AFTER_RUN_MS setting — not a script field.
function checkBrowserSettings(seg, runtime, where, report) {
  for (const k of ['mode', 'profile']) {
    if (seg[k] !== undefined && runtime !== 'browser')
      report.error(`${where}: "${k}" applies to a browser segment only.`);
  }
  if (seg.profile !== undefined && runtime === 'browser') {
    if (typeof seg.profile !== 'string' || !PROFILE_NAME.test(seg.profile.trim()))
      report.error(`${where}: "profile" must be 1 to 64 characters of letters, digits, dash, underscore or dot.`);
    else
      report.warn(`${where}: "profile" reuses the signed-in browser folder "${seg.profile.trim()}" on the worker. ` +
                  `Sign it in once with: dotnet run -- --login ${seg.profile.trim()}`);
  }
  if (seg.mode !== undefined && runtime === 'browser') {
    if (typeof seg.mode !== 'string' || !BROWSER_MODES.includes(seg.mode.trim().toLowerCase()))
      report.error(`${where}: "mode" must be ${BROWSER_MODES.map(m => `"${m}"`).join(' or ')}.`);
    else if (seg.mode !== seg.mode.trim().toLowerCase())
      report.warn(`${where}: write the mode in lower case ("${seg.mode.trim().toLowerCase()}").`);
  }
}

function validateSegmented(doc, report) {
  const declared = new Set();
  if (doc.params !== undefined) {
    if (doc.params === null || typeof doc.params !== 'object' || Array.isArray(doc.params)) report.error('"params" must be an object of name: value pairs');
    else for (const [k, v] of Object.entries(doc.params)) {
      declared.add(k);
      if (v !== null && !['string', 'number', 'boolean'].includes(typeof v))
        report.error(`params.${k} must be a string, number, boolean or null, got ${Array.isArray(v) ? 'array' : typeof v}`);
    }
  }
  if (!Array.isArray(doc.segments) || doc.segments.length === 0) {
    report.error('"segments" must be a non-empty array');
    return;
  }

  const produced = new Map(); // storeAs name -> { runtime, action, fields, sheetRoute, segment }
  const warnedParams = new Set();
  doc.segments.forEach((seg, i) => {
    const n = i + 1;
    const name = seg && typeof seg === 'object' && typeof seg.name === 'string' && seg.name ? ` "${seg.name}"` : '';
    const where = `segment ${n}${name}`;
    if (seg === null || typeof seg !== 'object' || Array.isArray(seg)) { report.error(`${where}: must be an object with "runtime" and "steps"`); return; }
    for (const k of Object.keys(seg)) if (!SEGMENT_FIELDS.includes(k)) report.error(`${where}: unknown field "${k}" (allowed: ${SEGMENT_FIELDS.join(', ')})`);
    if (seg.name !== undefined && typeof seg.name !== 'string') report.error(`${where}: "name" must be a string`);
    if (typeof seg.runtime !== 'string' || !RUNTIMES.includes(seg.runtime.trim().toLowerCase())) {
      report.error(`${where}: "runtime" must be one of ${RUNTIMES.join(', ')}`);
      return;
    }
    const runtime = seg.runtime.trim().toLowerCase();
    if (seg.runtime !== runtime) report.warn(`${where}: write the runtime in lower case ("${runtime}").`);
    if (runtime === 'terminal') report.warn(`${where}: terminal steps are not checked by this skill.`);
    checkBrowserSettings(seg, runtime, where, report);
    if (!Array.isArray(seg.steps) || seg.steps.length === 0) { report.error(`${where}: "steps" must be a non-empty array`); return; }

    const ctx = {
      report, runtime, segmented: true, declared, produced, warnedParams,
      local: new Map(), label: () => `${where}, `, segmentNumber: n,
    };
    checkSteps(seg.steps, ctx);
    for (const [k, v] of ctx.local) {
      if (produced.has(k)) report.warn(`${where}: storeAs "${k}" was already produced by segment ${produced.get(k).segment}; the later value is the one later segments see.`);
      produced.set(k, { ...v, segment: n });
    }
  });
}

// ── steps ───────────────────────────────────────────────────────────────────────────────────
function checkSteps(steps, ctx) {
  const { report } = ctx;
  steps.forEach((step, i) => {
    const where = `${ctx.label()}step ${i + 1}`;
    if (step === null || typeof step !== 'object' || Array.isArray(step)) { report.error(`${where}: must be an object`); return; }
    if (step.stepNumber !== i + 1) report.error(`${where}: stepNumber is ${JSON.stringify(step.stepNumber)}, expected ${i + 1} (1-based, in order, no gaps${ctx.segmented ? ', restarting at 1 in every segment' : ''})`);
    for (const k of Object.keys(step)) if (!STEP_FIELDS.includes(k)) report.error(`${where}: unknown field "${k}"`);
    if (typeof step.action !== 'string' || step.action === '') { report.error(`${where}: "action" is required`); return; }
    if (ctx.runtime === 'desktop') checkDesktopStep(step, where, ctx);
    else checkGenericStep(step, where, ctx);
  });
}

function checkGenericStep(step, where, ctx) {
  const { report } = ctx;
  const p = step.parameters;
  if (p === undefined) return;
  if (p === null || typeof p !== 'object' || Array.isArray(p)) { report.error(`${where} (${step.action}): "parameters" must be an object`); return; }
  for (const [k, v] of Object.entries(p)) {
    if (typeof v !== 'string') report.error(`${where} (${step.action}): parameter "${k}" must be a string, got ${typeof v}. Write "${v}" in quotes.`);
    else checkRefs(v, where, ctx);
  }
  if (p.storeAs !== undefined) defineStoreAs(p.storeAs, step, null, where, ctx);
}

function checkDesktopStep(step, where, ctx) {
  const { report } = ctx;
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
    if (['filePath', 'path', 'executable', 'args', 'url', 'window', 'windowTitle'].includes(k) && /[\n\r\t]/.test(v))
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
  if (step.action === 'file.read' && typeof p.path === 'string' && !p.path.includes('${') && !/^([A-Za-z]:[\\/]|%[A-Za-z_]+%|\\\\)/.test(p.path))
    report.warn(`${where}: file.read needs a full path (or %VARIABLE%\\...); "${p.path.slice(0, 60)}" is relative to the Host's working directory.`);

  // variable references
  for (const v of Object.values(p)) if (typeof v === 'string') checkRefs(v, where, ctx);

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
  }
  if (sheetRoute && !SHEET_FIELDS[sheetRoute]) report.error(`${where}: sheet-service has no route "${sheetRoute}". Use /batch, /update or /apply.`);

  if (p.storeAs !== undefined) defineStoreAs(p.storeAs, step, sheetRoute, where, ctx);
}

function defineStoreAs(name, step, sheetRoute, where, ctx) {
  const { report } = ctx;
  if (typeof name !== 'string' || !/^[A-Za-z][A-Za-z0-9_]*$/.test(name)) { if (name !== '') report.error(`${where}: storeAs "${name}" must be letters, digits and underscores`); return; }
  if (ctx.local.has(name)) report.warn(`${where}: storeAs "${name}" was already used by an earlier step and its outputs are overwritten`);
  ctx.local.set(name, {
    runtime: ctx.runtime, action: step.action,
    fields: sheetRoute ? SHEET_FIELDS[sheetRoute] : null, sheetRoute,
  });
}

// ── variable references ─────────────────────────────────────────────────────────────────────
function checkRefs(value, where, ctx) {
  for (const m of value.matchAll(/\$\{([^}]+)\}/g)) checkRef(m[1], where, ctx);
}

function checkRef(ref, where, ctx) {
  const { report } = ctx;
  if (BUILTINS.has(ref)) return;
  const desktop = ctx.runtime === 'desktop';

  if (ref.startsWith('param.')) { checkParamRef(ref, ref.slice(6), where, ctx); return; }

  if (!desktop) {
    // Browser and terminal steps: secrets and earlier storeAs names are valid; anything else is only a warning.
    if (/^secret:/i.test(ref)) {
      if (!/^secret:[A-Za-z0-9_]+$/i.test(ref)) report.warn(`${where}: "\${${ref}}" is not a valid secret reference; use \${secret:NAME} with letters, digits and underscores.`);
      return;
    }
    const base = ref.split('.')[0];
    if (!ctx.local.has(base)) report.warn(`${where}: "\${${ref}}" is used but no earlier step in this segment has storeAs "${base}".`);
    return;
  }

  const [ns, ...rest] = ref.split('.');
  const field = rest.join('.');
  const src = ctx.local.get(ns);
  if (!src) {
    if (ctx.produced.has(ns))
      report.error(`${where}: "\${${ref}}" was stored by segment ${ctx.produced.get(ns).segment}. Values from an earlier segment arrive as params: write "\${param.${ref}}".`);
    else
      report.error(`${where}: "\${${ref}}" is used but no earlier step has storeAs "${ns}". The text would be sent literally.`);
    return;
  }
  checkField(src, ns, field, ref, where, report);
}

function checkParamRef(ref, name, where, ctx) {
  const { report } = ctx;
  if (name === '') { report.warn(`${where}: "\${${ref}}" names no parameter.`); return; }
  if (ctx.declared.has(name)) return;
  const [base, ...rest] = name.split('.');
  const field = rest.join('.');
  const src = ctx.produced.get(base);
  if (src) {
    if (src.runtime === 'desktop') checkField(src, base, field, ref, where, report, true);
    return; // a browser step exposes its storeAs value as "name" and "name.value"; other fields are not listed here
  }
  if (ctx.warnedParams.has(name)) return;
  ctx.warnedParams.add(name);
  if (ctx.segmented)
    report.warn(`${where}: "\${${ref}}" is not declared in "params" and no earlier segment has storeAs "${base}". It must be supplied in the "params" of POST /jobs.`);
  else
    report.warn(`${where}: "\${${ref}}" is read from the job parameters. A flat script cannot declare them, so supply "${name}" in the "params" of POST /jobs.`);
}

function checkField(src, ns, field, ref, where, report, asParam) {
  const s = ACTIONS[src.action];
  const shown = asParam ? `\${param.${ns}.<field>}` : `\${${ns}.<field>}`;
  if (!field) { report.warn(`${where}: "\${${ref}}" has no field; use ${shown}`); return; }
  if (!s.outputs.includes(field) && !(src.fields && src.fields.includes(field)) && !s.flattensJson)
    report.warn(`${where}: step with storeAs "${ns}" (${src.action}) has no output "${field}". Outputs: ${s.outputs.join(', ') || 'none'}`);
  else if (s.flattensJson && !s.outputs.includes(field) && src.fields && !src.fields.includes(field))
    report.error(`${where}: "\${${ref}}" is not returned by ${src.sheetRoute}. Available: ${src.fields.join(', ')}`);
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
