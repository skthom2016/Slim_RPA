// The dummy client application's back end: a Vite dev-server middleware that reads and writes the
// hand-over folders under poc-data/. It plays the claims platform (claims) and the accounting
// system (bills). No database: the folders are the state.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const FOLDERS = [
  'claims/submitted', 'claims/approved', 'claims/processed',
  'bills/draft', 'bills/approved', 'bills/paid',
  'payments', 'exceptions',
];
const SAFE_ID = /^[A-Za-z0-9_.-]+$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

// A2 is started in NeeliRPA as an Event run: this app asks the Orchestrator to queue the A2 main job.
const A2_SCRIPT = 'finance-a2-payment-file';
const TERMINAL = ['SUCCEEDED', 'FAILED', 'CANCELLED', 'TIMEDOUT', 'HANDEDOFF', 'UNKNOWN'];

export function financeApi({ dataDir, seedDir, orchestratorUrl }) {
  const orch = String(orchestratorUrl || 'http://127.0.0.1:5080').replace(/\/+$/, '');
  const orchCall = async (method, pathname, payload) => {
    let res;
    try {
      res = await fetch(orch + pathname, {
        method,
        headers: payload ? { 'Content-Type': 'application/json' } : undefined,
        body: payload ? JSON.stringify(payload) : undefined,
        signal: AbortSignal.timeout(10000),
      });
    } catch {
      throw Object.assign(new Error(`NeeliRPA is not reachable at ${orch}. Start the Orchestrator and try again.`), { status: 502 });
    }
    const text = await res.text();
    let data = {};
    try { data = text ? JSON.parse(text) : {}; } catch { /* not JSON */ }
    if (!res.ok) {
      const why = data.message || data.error || text || res.statusText;
      throw Object.assign(new Error(`NeeliRPA refused the request (${res.status}): ${why}`), { status: 502 });
    }
    return data;
  };
  const p = (...parts) => path.join(dataDir, ...parts);

  const ensure = () => FOLDERS.forEach((f) => fs.mkdirSync(p(f), { recursive: true }));

  const readJson = (file) => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''));
  const writeJson = (file, v) => {
    fs.writeFileSync(file + '.tmp', JSON.stringify(v, null, 2) + '\n');
    fs.renameSync(file + '.tmp', file);
  };
  const jsonFiles = (folder) =>
    fs.existsSync(p(folder)) ? fs.readdirSync(p(folder)).filter((n) => n.toLowerCase().endsWith('.json')).sort() : [];
  const readAll = (folder) =>
    jsonFiles(folder).flatMap((n) => {
      try { return [{ file: n, data: readJson(p(folder, n)) }]; } catch { return []; }
    });

  const reference = () => ({
    employees: readJson(path.join(seedDir, 'employees.json')),
    categories: readJson(path.join(seedDir, 'categories.json')),
    // Bank details stay in the accounting system; the app only shows whether they are complete.
    payees: readJson(path.join(seedDir, 'payees.json')).map((x) => ({
      employeeId: x.employeeId, name: x.name,
      bankDetails: x.bankCode && x.accountNumber ? 'complete' : 'incomplete',
    })),
  });

  const billFolder = (billId) =>
    ['bills/draft', 'bills/approved', 'bills/paid'].find((f) => fs.existsSync(p(f, billId + '.json')));

  // Every claim with where it is now.
  const claims = () => {
    const out = [];
    const add = (folder, status) => readAll(folder).forEach(({ data }) => {
      const billId = 'BILL-' + data.claimId;
      out.push({ ...data, status, billId: status === 'processed' ? billId : undefined, billStatus: status === 'processed' ? (billFolder(billId) || '').replace('bills/', '') : undefined });
    });
    add('claims/submitted', 'submitted');
    add('claims/approved', 'approved');
    add('claims/processed', 'processed');
    readAll('exceptions').forEach(({ data }) => {
      if (data.kind === 'claim' && data.claim && typeof data.claim === 'object') {
        out.push({ ...data.claim, claimId: data.claimId, status: 'exception', reason: data.reason, exceptionAt: data.at });
      }
    });
    return out.sort((a, b) => (a.claimId < b.claimId ? 1 : -1));
  };

  const nextClaimId = () => {
    const max = claims().reduce((m, c) => {
      const n = parseInt(String(c.claimId).replace(/^CLM-/, ''), 10);
      return Number.isFinite(n) && n > m ? n : m;
    }, 0);
    return 'CLM-' + String(max + 1).padStart(4, '0');
  };

  const bills = () => {
    const list = (folder) => readAll(folder).map(({ data }) => data).sort((a, b) => (a.billId < b.billId ? -1 : 1));
    return { draft: list('bills/draft'), approved: list('bills/approved'), paid: list('bills/paid') };
  };

  const payments = () =>
    fs.readdirSync(p('payments'))
      .filter((n) => n.toLowerCase().endsWith('.csv'))
      .sort().reverse()
      .map((n) => {
        const st = fs.statSync(p('payments', n));
        const sumFile = p('payments', n.replace(/\.csv$/i, '.summary.json'));
        let summary = null;
        try { summary = readJson(sumFile); } catch { /* no summary */ }
        return { file: n, size: st.size, modified: st.mtime.toISOString(), summary };
      });

  const reset = () => {
    // Only the hand-over folders are emptied; nothing outside poc-data is touched.
    for (const top of ['claims', 'bills', 'payments', 'exceptions']) {
      fs.rmSync(p(top), { recursive: true, force: true });
    }
    ensure();
    const seedClaims = path.join(seedDir, 'claims-submitted');
    for (const n of fs.readdirSync(seedClaims).filter((x) => x.endsWith('.json'))) {
      fs.copyFileSync(path.join(seedClaims, n), p('claims/submitted', n));
    }
  };

  const send = (res, status, body) => {
    res.statusCode = status;
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(body));
  };
  const body = (req) => new Promise((resolve, reject) => {
    let s = '';
    req.on('data', (c) => { s += c; if (s.length > 1e6) req.destroy(); });
    req.on('end', () => { try { resolve(s ? JSON.parse(s) : {}); } catch (e) { reject(e); } });
    req.on('error', reject);
  });

  async function handle(req, res) {
    const url = new URL(req.url, 'http://localhost');
    const parts = url.pathname.replace(/^\/api\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);
    const m = req.method;

    if (m === 'GET' && parts[0] === 'reference') return send(res, 200, reference());

    if (parts[0] === 'claims') {
      if (m === 'GET' && parts.length === 1) return send(res, 200, claims());
      if (m === 'POST' && parts.length === 1) {
        const b = await body(req);
        const emp = reference().employees.find((e) => e.employeeId === b.employeeId);
        if (!emp) return send(res, 400, { error: 'Choose an employee.' });
        const items = Array.isArray(b.items) ? b.items : [];
        if (items.length === 0) return send(res, 400, { error: 'Add at least one item.' });
        const claim = {
          claimId: nextClaimId(), employeeId: emp.employeeId, employeeName: emp.name,
          submittedAt: new Date().toISOString(),
          items: items.map((i) => ({
            date: String(i.date || ''), category: String(i.category || ''),
            amount: Math.round(Number(i.amount) * 100) / 100 || 0, description: String(i.description || '').trim(),
          })),
        };
        writeJson(p('claims/submitted', claim.claimId + '.json'), claim);
        return send(res, 201, claim);
      }
      if (m === 'POST' && parts.length === 3 && parts[2] === 'approve') {
        const id = parts[1];
        if (!SAFE_ID.test(id)) return send(res, 400, { error: 'Not a claim id.' });
        const src = p('claims/submitted', id + '.json');
        if (!fs.existsSync(src)) return send(res, 404, { error: `Claim ${id} is not waiting for approval.` });
        if (claims().some((c) => c.claimId === id && c.status !== 'submitted')) {
          return send(res, 409, { error: `Claim ${id} has already been approved.` });
        }
        const claim = readJson(src);
        claim.approvedAt = new Date().toISOString();
        claim.approvedBy = String((await body(req)).approvedBy || 'Finance Officer');
        writeJson(p('claims/approved', id + '.json'), claim);
        fs.rmSync(src);
        return send(res, 200, claim);
      }
    }

    if (parts[0] === 'bills') {
      if (m === 'GET' && parts.length === 1) return send(res, 200, bills());
      const id = parts[1];
      if (id && !SAFE_ID.test(id)) return send(res, 400, { error: 'Not a bill id.' });
      if (m === 'GET' && parts[2] === 'pdf') {
        const f = p('bills/draft', id + '.pdf');
        if (!fs.existsSync(f)) return send(res, 404, { error: `No PDF for ${id}.` });
        res.setHeader('Content-Type', 'application/pdf');
        res.setHeader('Content-Disposition', `inline; filename="${id}.pdf"`);
        return fs.createReadStream(f).pipe(res);
      }
      if (m === 'POST' && parts[2] === 'approve') {
        const { paymentDate } = await body(req);
        if (!DATE.test(String(paymentDate || ''))) return send(res, 400, { error: 'Pick a payment date.' });
        const src = p('bills/draft', id + '.json');
        if (!fs.existsSync(src)) return send(res, 404, { error: `Bill ${id} is not a draft.` });
        const bill = readJson(src);
        Object.assign(bill, { status: 'approved', approvedAt: new Date().toISOString(), paymentDate });
        writeJson(p('bills/approved', id + '.json'), bill);
        fs.rmSync(src);
        return send(res, 200, bill);
      }
    }

    if (parts[0] === 'payments') {
      if (m === 'GET' && parts.length === 1) return send(res, 200, payments());
      const file = parts[1];
      if (m === 'GET' && file && SAFE_ID.test(file) && /\.(csv|json)$/i.test(file) && fs.existsSync(p('payments', file))) {
        res.setHeader('Content-Type', file.endsWith('.csv') ? 'text/csv; charset=utf-8' : 'application/json');
        res.setHeader('Content-Disposition', `${url.searchParams.get('download') ? 'attachment' : 'inline'}; filename="${file}"`);
        return fs.createReadStream(p('payments', file)).pipe(res);
      }
      return send(res, 404, { error: 'No such payment file.' });
    }

    // Create payment file: queue A2 for one payment date.
    if (m === 'POST' && parts[0] === 'payruns' && parts.length === 1) {
      const { paymentDate } = await body(req);
      if (!DATE.test(String(paymentDate || ''))) return send(res, 400, { error: 'Pick a payment date.' });
      const due = bills().approved.filter((x) => x.paymentDate === paymentDate);
      if (due.length === 0) return send(res, 409, { error: `No approved bills to pay on ${paymentDate}.` });
      // The same bills for the same date make the same event, so a second click does not start a second run.
      const hash = crypto.createHash('sha1').update(due.map((x) => x.billId).sort().join(',')).digest('hex').slice(0, 12);
      const job = await orchCall('POST', '/jobs', {
        schemaVersion: '1',
        scriptArtifactId: A2_SCRIPT,
        scriptVersion: 'latest',
        triggerType: 'Event',
        eventId: `payrun-${paymentDate}-${hash}`,
        params: { paymentDate },
      });
      return send(res, 202, { runId: job.runId, state: job.state, paymentDate, bills: due.length });
    }

    // Progress of a Create payment file run, and its summary once finished.
    if (m === 'GET' && parts[0] === 'payruns' && parts.length === 2) {
      const job = await orchCall('GET', '/jobs/' + encodeURIComponent(parts[1]));
      const state = String(job.state || '').toUpperCase();
      const outputs = [job.outputs, ...(job.segments || []).map((x) => x.outputs)].filter(Boolean);
      const found = outputs.map((o) => o['a2.message'] ?? o.message).find((x) => x);
      return send(res, 200, { runId: parts[1], state, done: TERMINAL.includes(state), message: found || '' });
    }

    if (m === 'POST' && parts[0] === 'reset') {
      reset();
      return send(res, 200, { message: 'Demo data reset: folders emptied, seeded claims restored.' });
    }

    return send(res, 404, { error: 'Not found.' });
  }

  return {
    name: 'finance-poc-api',
    configureServer(server) {
      // First start: every folder empty or missing, so begin with the seeded claims.
      const empty = FOLDERS.every((f) => !fs.existsSync(p(f)) || fs.readdirSync(p(f)).length === 0);
      if (empty) reset();
      ensure();
      server.middlewares.use('/api', (req, res) => {
        req.url = '/api' + req.url;
        handle(req, res).catch((e) => send(res, e.status || 500, { error: e.message }));
      });
    },
  };
}
