#!/usr/bin/env node
/**
 * Offline validator for Tracto browser scripts.
 *
 *   node validate.js <file>.json
 *
 * Checks a flat browser script, or a segmented file where browser segments are checked in
 * full and desktop segments for structure and variable references only (the desktop skill's
 * validator is the one that checks those properly).
 *
 * Every rule here mirrors TractoRuntime.Browser.Host/StepTranslator.cs, which rejects a bad
 * script before a browser is opened.
 */
const fs = require('fs');
const path = require('path');

const META = JSON.parse(fs.readFileSync(path.join(__dirname, 'actions.json'), 'utf8'));
const ACTIONS = META.actions;
const BUILTINS = new Set(['now.iso', 'now.local', 'now.date', 'now.time']);
const RUNTIMES = ['desktop', 'browser', 'terminal'];
const SEGMENT_FIELDS = ['name', 'runtime', 'mode', 'profile', 'steps'];
const STEP_FIELDS = ['stepNumber', 'action', 'description', 'parameters', 'condition'];
const BROWSER_MODES = ['headed', 'headless'];
const PROFILE_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
const SECRET_REF = /\$\{secret:([A-Za-z0-9_]*)\}/gi;

// short name -> spec, including every alias the host accepts
const BY_NAME = new Map();
for (const [name, spec] of Object.entries(ACTIONS)) {
  BY_NAME.set(name.toLowerCase(), { name, spec });
  BY_NAME.set(spec.canonical.toLowerCase(), { name, spec });
  for (const a of spec.aliases) BY_NAME.set(a.toLowerCase(), { name, spec });
}

function lookup(action) {
  return typeof action === 'string' ? BY_NAME.get(action.trim().toLowerCase()) : undefined;
}

class Report {
  constructor(file) { this.file = file; this.errors = []; this.warnings = []; }
  error(m) { this.errors.push(m); }
  warn(m) { this.warnings.push(m); }
  print() {
    console.log(`${this.file}: ${this.errors.length} error(s), ${this.warnings.length} warning(s)`);
    for (const e of this.errors) console.log(`  ERROR  ${e}`);
    for (const w of this.warnings) console.log(`  warn   ${w}`);
    return this.errors.length === 0;
  }
}

function validate(file) {
  const report = new Report(file);
  let raw;
  try { raw = fs.readFileSync(file, 'utf8'); }
  catch (e) { report.error(`cannot read file: ${e.message}`); return report; }

  let doc;
  try { doc = JSON.parse(raw); }
  catch (e) { report.error(`not valid JSON: ${e.message}`); return report; }

  if (doc === null || typeof doc !== 'object' || Array.isArray(doc)) {
    report.error('the file must contain a JSON object'); return report;
  }
  if (doc.schemaVersion !== undefined || doc.payload !== undefined || doc.script !== undefined) {
    report.error('this is a publishing envelope, not a script. Write the script itself; publishing adds the envelope.');
    return report;
  }

  for (const f of ['id', 'name', 'version']) {
    if (typeof doc[f] !== 'string' || doc[f] === '') report.error(`"${f}" is required`);
  }
  if (typeof doc.id === 'string' && !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(doc.id))
    report.error(`"id" must be lowercase letters, digits and hyphens: got "${doc.id}"`);
  if (typeof doc.version === 'string' && !/^\d+\.\d+\.\d+$/.test(doc.version))
    report.error(`"version" must look like 1.0.0: got "${doc.version}"`);

  if (doc.params !== undefined) {
    if (doc.params === null || typeof doc.params !== 'object' || Array.isArray(doc.params)) {
      report.error('"params" must be an object of name/value pairs');
    } else {
      for (const [k, v] of Object.entries(doc.params)) {
        if (k === '') report.error('a parameter name is empty');
        if (typeof v !== 'string') report.error(`param "${k}" must be a string, got ${typeof v}`);
      }
    }
  }

  const segmented = doc.segments !== undefined;
  if (segmented && doc.steps !== undefined)
    report.error('a file has either "segments" or "steps", never both');
  if (!segmented && doc.steps === undefined)
    report.error('a file needs "segments" (one per runtime stage) or "steps" (a single browser script)');

  if (segmented) validateSegmented(doc, report);
  else validateFlat(doc, report);

  return report;
}

function validateFlat(doc, report) {
  for (const k of Object.keys(doc)) {
    if (!['id', 'name', 'version', 'targetRuntime', 'params', 'steps'].includes(k))
      report.error(`unknown top-level field "${k}"`);
  }
  const runtime = String(doc.targetRuntime || '').toLowerCase();
  if (!runtime) report.error('"targetRuntime" is required in a flat script (use "browser")');
  else if (!RUNTIMES.includes(runtime)) report.error(`"targetRuntime" must be one of ${RUNTIMES.join(', ')}`);
  else if (runtime !== 'browser')
    report.warn(`targetRuntime "${runtime}": this skill checks browser steps. Use the desktop skill for a desktop script.`);

  if (!Array.isArray(doc.steps) || doc.steps.length === 0) {
    report.error('"steps" must be a non-empty array'); return;
  }
  const ctx = { report, runtime, segmented: false, produced: new Map(), label: () => '', params: doc.params || {} };
  checkSteps(doc.steps, ctx);
}

function validateSegmented(doc, report) {
  for (const k of Object.keys(doc)) {
    if (!['id', 'name', 'version', 'params', 'segments'].includes(k))
      report.error(`unknown top-level field "${k}" (a segmented file has no targetRuntime and no steps)`);
  }
  if (!Array.isArray(doc.segments) || doc.segments.length === 0) {
    report.error('"segments" must be a non-empty array'); return;
  }

  const produced = new Map();           // storeAs -> {segment}
  doc.segments.forEach((seg, i) => {
    const where = `segment ${i + 1}${seg && seg.name ? ` "${seg.name}"` : ''}`;
    if (seg === null || typeof seg !== 'object' || Array.isArray(seg)) {
      report.error(`${where}: must be an object`); return;
    }
    for (const k of Object.keys(seg)) {
      if (!SEGMENT_FIELDS.includes(k))
        report.error(`${where}: unknown field "${k}" (allowed: ${SEGMENT_FIELDS.join(', ')})`);
    }
    const runtime = String(seg.runtime || '').toLowerCase();
    if (!runtime) report.error(`${where}: "runtime" is required`);
    else if (!RUNTIMES.includes(runtime)) report.error(`${where}: runtime must be one of ${RUNTIMES.join(', ')}`);
    else if (seg.runtime !== runtime) report.warn(`${where}: write the runtime in lower case ("${runtime}")`);

    checkBrowserSettings(seg, runtime, where, report);

    if (!Array.isArray(seg.steps) || seg.steps.length === 0) {
      report.error(`${where}: "steps" must be a non-empty array`); return;
    }
    const ctx = {
      report, runtime, segmented: true, produced,
      segmentIndex: i + 1, label: () => `${where}, `, params: doc.params || {},
    };
    checkSteps(seg.steps, ctx);
  });
}

function checkBrowserSettings(seg, runtime, where, report) {
  if (seg.mode !== undefined) {
    if (runtime !== 'browser') report.error(`${where}: "mode" applies to a browser segment only`);
    else if (typeof seg.mode !== 'string' || !BROWSER_MODES.includes(seg.mode.trim().toLowerCase()))
      report.error(`${where}: "mode" must be "headed" or "headless"`);
    else if (seg.mode !== seg.mode.trim().toLowerCase())
      report.warn(`${where}: write the mode in lower case ("${seg.mode.trim().toLowerCase()}")`);
  }
  if (seg.profile !== undefined) {
    if (runtime !== 'browser') report.error(`${where}: "profile" applies to a browser segment only`);
    else if (typeof seg.profile !== 'string' || !PROFILE_NAME.test(seg.profile.trim()))
      report.error(`${where}: "profile" must be letters, digits, dot, dash or underscore`);
    else
      report.warn(`${where}: "profile" uses the signed-in browser folder "${seg.profile.trim()}" on the worker. ` +
        `Sign it in once with: dotnet run -- --login ${seg.profile.trim()} <url>. Only one run at a time may use it.`);
  }
}

function checkSteps(steps, ctx) {
  const { report } = ctx;
  steps.forEach((step, i) => {
    const where = `${ctx.label()}step ${i + 1}`;
    if (step === null || typeof step !== 'object' || Array.isArray(step)) {
      report.error(`${where}: must be an object`); return;
    }
    if (step.stepNumber !== i + 1)
      report.error(`${where}: stepNumber is ${JSON.stringify(step.stepNumber)}, expected ${i + 1}` +
        (ctx.segmented ? ' (restarting at 1 in every segment)' : ''));
    for (const k of Object.keys(step))
      if (!STEP_FIELDS.includes(k)) report.error(`${where}: unknown field "${k}"`);
    if (typeof step.action !== 'string' || step.action === '') {
      report.error(`${where}: "action" is required`); return;
    }
    if (ctx.runtime === 'browser') checkBrowserStep(step, where, ctx);
    else checkOtherRuntimeStep(step, where, ctx);
  });
}

/** A segment of another runtime: structure and variable references only. */
function checkOtherRuntimeStep(step, where, ctx) {
  const p = step.parameters;
  if (p === undefined) return;
  if (p === null || typeof p !== 'object' || Array.isArray(p)) {
    ctx.report.error(`${where} (${step.action}): "parameters" must be an object`); return;
  }
  for (const [k, v] of Object.entries(p)) {
    if (typeof v !== 'string') ctx.report.error(`${where} (${step.action}): parameter "${k}" must be a string`);
    else checkRefs(v, where, ctx);
  }
  if (p.storeAs) defineStoreAs(p.storeAs, null, where, ctx);
}

function checkBrowserStep(step, where, ctx) {
  const { report } = ctx;
  const found = lookup(step.action);
  if (!found) {
    report.error(`${where}: unknown action "${step.action}". Known: ${Object.keys(ACTIONS).join(', ')}`);
    return;
  }
  const { name, spec } = found;
  if (step.action.toLowerCase().startsWith('desktop.'))
    report.error(`${where}: "${step.action}" is a desktop action; a browser segment cannot contain one`);

  const p = step.parameters;
  if (p === undefined || p === null || typeof p !== 'object' || Array.isArray(p)) {
    if (spec.required.length || spec.element === 'required')
      report.error(`${where} (${name}): "parameters" object is required`);
    return;
  }
  for (const [k, v] of Object.entries(p)) {
    if (typeof v !== 'string') report.error(`${where} (${name}): parameter "${k}" must be a string, got ${typeof v}. Write "${v}" in quotes.`);
    else checkRefs(v, where, ctx);
  }

  for (const r of spec.required)
    if (!p[r]) report.error(`${where} (${name}): required parameter "${r}" is missing`);

  const known = new Set([...spec.required, ...spec.optional, ...META.selectors, 'storeAs']);
  for (const k of Object.keys(p))
    if (!known.has(k)) report.warn(`${where} (${name}): parameter "${k}" is not used by ${name} and is ignored`);

  checkSelector(name, spec, p, where, report);

  if (name === 'navigate' && p.url && !p.url.includes('${') && !/^[a-z][a-z0-9+.-]*:\/\//i.test(p.url))
    report.error(`${where}: "url" must be absolute (https://...), got "${p.url}"`);

  if (name === 'delay' && p.ms && !p.ms.includes('${')) {
    if (!/^\d+$/.test(p.ms)) report.error(`${where}: "ms" must be a whole number in a string, got "${p.ms}"`);
    else if (Number(p.ms) > META.maxDelayMs) report.warn(`${where}: a browser delay is capped at ${META.maxDelayMs} ms`);
  }
  if (p.timeoutMs && !p.timeoutMs.includes('${') && !/^[1-9]\d*$/.test(p.timeoutMs))
    report.error(`${where}: "timeoutMs" must be a positive whole number in a string, got "${p.timeoutMs}"`);
  if (p.limit && !p.limit.includes('${') && !/^\d+$/.test(p.limit))
    report.error(`${where}: "limit" must be a non-negative whole number in a string, got "${p.limit}"`);

  if (name === 'assert') checkAssert(p, where, report);

  if (p.storeAs) {
    if (!spec.outputs.length)
      report.warn(`${where} (${name}): "${name}" produces no value, so storeAs publishes nothing`);
    defineStoreAs(p.storeAs, spec.outputs, where, ctx);
  }

  for (const [k, v] of Object.entries(p)) {
    if (typeof v !== 'string') continue;
    SECRET_REF.lastIndex = 0;
    let m;
    while ((m = SECRET_REF.exec(v)) !== null) {
      if (!m[1]) report.error(`${where}: "\${secret:}" needs a name, for example "\${secret:MY_PASSWORD}"`);
      else report.warn(`${where}: "${k}" reads secret "${m[1]}". Set TRACTO_SECRET_${m[1].toUpperCase()} on the machine ` +
        `that runs the browser worker. The run is refused before a browser opens if it is missing.`);
    }
  }

  // A password typed in as text. The giveaway is the field it is going into, not the
  // parameter name: the value always arrives as "value", on a field labelled Password.
  const SECRETISH = /password|passwd|pwd|passcode|otp|secret|api[_-]?key|token/i;
  const fieldNames = [p.label, p.placeholder, p.name, p.testId, p.text, p.css, p.xpath]
    .filter(v => typeof v === 'string');
  const looksLikeSecretField = fieldNames.some(v => SECRETISH.test(v));
  for (const k of ['value', 'key']) {
    const v = p[k];
    if (typeof v !== 'string' || v.trim() === '') continue;
    if (v.includes('${secret:') || v.includes('${param.')) continue;
    if (looksLikeSecretField || SECRETISH.test(k))
      report.error(`${where}: "${k}" puts a literal value into what looks like a secret field ` +
        `(${fieldNames.filter(f => SECRETISH.test(f)).join(', ') || k}). A script is stored and readable: ` +
        `use "\${secret:NAME}" with TRACTO_SECRET_NAME set on the worker, or sign the browser profile in once.`);
  }
}

function checkSelector(name, spec, p, where, report) {
  const named = META.elementSelectors.filter(s => p[s]);
  const needs = spec.element === 'required'
    || (spec.element === 'kind' && p.kind && !META.assertKindsWithoutElement.includes(String(p.kind).toLowerCase()));

  if (p.name && !p.role)
    report.error(`${where} (${name}): "name" only applies together with "role". Add a role, or name the element another way.`);

  if (needs && named.length === 0)
    report.error(`${where} (${name}): needs a target. Give one of ${META.elementSelectors.join(', ')} (role may add name).`);
  if (named.length > 1)
    report.warn(`${where} (${name}): the element is named ${named.length} ways (${named.join(', ')}); ` +
      `the first that matches exactly one element wins, in the order ${META.elementSelectors.join(' > ')}.`);
  if (spec.element === 'no' && named.length)
    report.warn(`${where} (${name}): ${name} does not act on an element, so ${named.join(', ')} is ignored`);
}

function checkAssert(p, where, report) {
  const kind = String(p.kind || '').toLowerCase();
  if (p.kind && !META.assertKinds.map(k => k.toLowerCase()).includes(kind))
    report.error(`${where}: unknown assert kind "${p.kind}". Known: ${META.assertKinds.join(', ')}`);
  if (META.assertKindsNeedingExpected.includes(kind) && !p.expected)
    report.error(`${where}: assert kind "${p.kind}" needs "expected"`);
}

function defineStoreAs(storeAs, outputs, where, ctx) {
  if (typeof storeAs !== 'string' || storeAs === '') {
    ctx.report.error(`${where}: "storeAs" must be a non-empty name`); return;
  }
  const prev = ctx.produced.get(storeAs);
  if (prev && prev.segment !== ctx.segmentIndex)
    ctx.report.warn(`${where}: storeAs "${storeAs}" was already published by segment ${prev.segment}; later segments see the later value`);
  ctx.produced.set(storeAs, { segment: ctx.segmentIndex, outputs: outputs || [] });
}

/** Every ${...} reference must be a builtin, a param, a secret, or something published earlier. */
function checkRefs(value, where, ctx) {
  const re = /\$\{([^}]*)\}/g;
  let m;
  while ((m = re.exec(value)) !== null) {
    const ref = m[1].trim();
    if (!ref) { ctx.report.error(`${where}: empty "\${}" reference`); continue; }
    if (/^secret:/i.test(ref)) continue;
    if (BUILTINS.has(ref)) continue;

    if (ref.startsWith('param.')) {
      const rest = ref.slice('param.'.length);
      const root = rest.split('.')[0];
      if (Object.prototype.hasOwnProperty.call(ctx.params, root)) continue;
      const prev = ctx.produced.get(root);
      if (prev && (!ctx.segmented || prev.segment < ctx.segmentIndex)) continue;
      if (prev && ctx.segmented && prev.segment === ctx.segmentIndex) {
        ctx.report.error(`${where}: "\${${ref}}" is published in this same segment, so it is read without the param prefix: "\${${rest}}"`);
        continue;
      }
      ctx.report.warn(`${where}: "\${${ref}}" is not in params and nothing before it publishes "${root}". ` +
        `A job may still supply it; an unresolved reference is left in the text as written.`);
      continue;
    }

    const root = ref.split('.')[0];
    const prev = ctx.produced.get(root);
    if (!prev) {
      ctx.report.error(`${where}: "\${${ref}}" is not defined. Publish it earlier with storeAs, or use \${param.${ref}} if it comes from an earlier segment.`);
      continue;
    }
    if (ctx.segmented && prev.segment !== ctx.segmentIndex)
      ctx.report.error(`${where}: "\${${ref}}" comes from segment ${prev.segment}, so it is read as "\${param.${ref}}"`);
  }
}

const file = process.argv[2];
if (!file) {
  console.error('usage: node validate.js <script.json>');
  process.exit(2);
}
process.exit(validate(file).print() ? 0 : 1);
