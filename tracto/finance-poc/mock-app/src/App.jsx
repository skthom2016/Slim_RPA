import React, { useCallback, useEffect, useState } from 'react';

// ---------- helpers ----------

async function api(path, options = {}) {
  const res = await fetch('/api' + path, {
    ...options,
    headers: options.body ? { 'Content-Type': 'application/json' } : undefined,
  });
  const data = res.headers.get('content-type')?.includes('json') ? await res.json() : await res.text();
  if (!res.ok) throw new Error(data?.error || res.statusText);
  return data;
}

const money = (n) => Number(n || 0).toFixed(2);
const sum = (items) => items.reduce((t, i) => t + (Number(i.amount) || 0), 0);
const today = () => new Date().toISOString().slice(0, 10);
const when = (iso) => (iso ? new Date(iso).toLocaleString() : '');

// Re-reads data every few seconds, so files written by NeeliRPA appear without a reload.
function usePoll(load, ms = 3000) {
  const [data, setData] = useState(null);
  const [error, setError] = useState('');
  const refresh = useCallback(() => load().then((d) => { setData(d); setError(''); }).catch((e) => setError(e.message)), [load]);
  useEffect(() => {
    refresh();
    const t = setInterval(refresh, ms);
    return () => clearInterval(t);
  }, [refresh, ms]);
  return [data, refresh, error];
}

function Badge({ status }) {
  const labels = {
    submitted: 'Submitted', approved: 'Approved', processed: 'Bill created', exception: 'Needs attention',
    draft: 'Draft', paid: 'Paid',
  };
  return <span className={`badge badge-${status}`}>{labels[status] || status}</span>;
}

// ---------- sign-in ----------

function SignIn({ reference, onSignIn }) {
  const [role, setRole] = useState('Employee');
  const [employeeId, setEmployeeId] = useState('');
  const employees = reference?.employees || [];
  const submit = (e) => {
    e.preventDefault();
    if (role === 'Employee') {
      const emp = employees.find((x) => x.employeeId === employeeId);
      if (!emp) return;
      onSignIn({ role, name: emp.name, employeeId: emp.employeeId });
    } else {
      onSignIn({ role, name: 'Finance Officer' });
    }
  };
  return (
    <div className="signin">
      <form className="card signin-card" onSubmit={submit}>
        <h1>Finance Portal</h1>
        <p className="muted">Demo Organisation</p>
        <label>Sign in as
          <select value={role} onChange={(e) => setRole(e.target.value)} data-testid="role">
            <option>Employee</option>
            <option>Finance Officer</option>
          </select>
        </label>
        {role === 'Employee' && (
          <label>Employee
            <select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} data-testid="employee">
              <option value="">Choose…</option>
              {employees.map((e) => <option key={e.employeeId} value={e.employeeId}>{e.name} ({e.employeeId})</option>)}
            </select>
          </label>
        )}
        <button className="primary" disabled={role === 'Employee' && !employeeId}>Sign in</button>
        <p className="muted small">Demo sign-in: no password.</p>
      </form>
    </div>
  );
}

// ---------- claims ----------

const blankItem = () => ({ date: today(), category: '', amount: '', description: '' });

function NewClaim({ user, categories, onDone, notify }) {
  const [items, setItems] = useState([blankItem()]);
  const [busy, setBusy] = useState(false);
  const set = (i, k, v) => setItems(items.map((it, j) => (j === i ? { ...it, [k]: v } : it)));
  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      const c = await api('/claims', { method: 'POST', body: JSON.stringify({ employeeId: user.employeeId, items }) });
      notify(`Claim ${c.claimId} submitted, total ${money(sum(c.items))}.`);
      setItems([blankItem()]);
      onDone();
    } catch (err) {
      notify(err.message, true);
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="card" onSubmit={submit}>
      <h2>New expense claim</h2>
      <table className="grid">
        <thead><tr><th>Date</th><th>Category</th><th>Amount</th><th>Description</th><th /></tr></thead>
        <tbody>
          {items.map((it, i) => (
            <tr key={i}>
              <td><input type="date" value={it.date} onChange={(e) => set(i, 'date', e.target.value)} required /></td>
              <td>
                <select value={it.category} onChange={(e) => set(i, 'category', e.target.value)} required>
                  <option value="">Choose…</option>
                  {categories.map((c) => <option key={c.name}>{c.name}</option>)}
                </select>
              </td>
              <td><input type="number" step="0.01" min="0.01" value={it.amount} onChange={(e) => set(i, 'amount', e.target.value)} required className="num" /></td>
              <td><input value={it.description} onChange={(e) => set(i, 'description', e.target.value)} required placeholder="What was it for?" /></td>
              <td>{items.length > 1 && <button type="button" className="link" onClick={() => setItems(items.filter((_, j) => j !== i))}>Remove</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="row">
        <button type="button" onClick={() => setItems([...items, blankItem()])}>+ Add item</button>
        <span className="spacer" />
        <strong>Total {money(sum(items))}</strong>
        <button className="primary" disabled={busy}>Submit claim</button>
      </div>
    </form>
  );
}

function ClaimItems({ items }) {
  return (
    <table className="grid inner">
      <tbody>
        {items.map((it, i) => (
          <tr key={i}><td>{it.date}</td><td>{it.category}</td><td>{it.description}</td><td className="num">{money(it.amount)}</td></tr>
        ))}
      </tbody>
    </table>
  );
}

function ClaimsTable({ claims, showEmployee, actions }) {
  const [open, setOpen] = useState({});
  if (!claims.length) return <p className="muted">No claims.</p>;
  return (
    <table className="grid">
      <thead>
        <tr><th>Claim</th>{showEmployee && <th>Employee</th>}<th>Submitted</th><th className="num">Total</th><th>Status</th><th /></tr>
      </thead>
      <tbody>
        {claims.map((c) => (
          <React.Fragment key={c.claimId + c.status}>
            <tr data-testid={`claim-${c.claimId}`}>
              <td><button className="link" onClick={() => setOpen({ ...open, [c.claimId]: !open[c.claimId] })}>{open[c.claimId] ? '▾' : '▸'} {c.claimId}</button></td>
              {showEmployee && <td>{c.employeeName}</td>}
              <td>{when(c.submittedAt)}</td>
              <td className="num">{money(sum(c.items || []))}</td>
              <td>
                <Badge status={c.status} />
                {c.status === 'processed' && <span className="muted small"> {c.billId} ({c.billStatus})</span>}
                {c.status === 'exception' && <div className="reason">{c.reason}</div>}
              </td>
              <td className="actions">{actions && actions(c)}</td>
            </tr>
            {open[c.claimId] && (
              <tr className="detail"><td colSpan={showEmployee ? 6 : 5}><ClaimItems items={c.items || []} /></td></tr>
            )}
          </React.Fragment>
        ))}
      </tbody>
    </table>
  );
}

function ClaimsPage({ user, reference, notify }) {
  const load = useCallback(() => api('/claims'), []);
  const [claims, refresh] = usePoll(load);
  const list = claims || [];

  if (user.role === 'Employee') {
    return (
      <>
        <NewClaim user={user} categories={reference.categories} onDone={refresh} notify={notify} />
        <div className="card">
          <h2>My claims</h2>
          <ClaimsTable claims={list.filter((c) => c.employeeId === user.employeeId)} />
        </div>
      </>
    );
  }

  const approve = async (c) => {
    try {
      await api(`/claims/${c.claimId}/approve`, { method: 'POST', body: JSON.stringify({ approvedBy: user.name }) });
      notify(`Claim ${c.claimId} approved. It is now in claims/approved for NeeliRPA.`);
      refresh();
    } catch (err) {
      notify(err.message, true);
    }
  };
  const waiting = list.filter((c) => c.status === 'submitted');
  return (
    <>
      <div className="card">
        <h2>Waiting for approval <span className="count">{waiting.length}</span></h2>
        <ClaimsTable claims={waiting} showEmployee actions={(c) => <button className="primary" onClick={() => approve(c)}>Approve</button>} />
      </div>
      <div className="card">
        <h2>All claims</h2>
        <ClaimsTable claims={list.filter((c) => c.status !== 'submitted')} showEmployee />
      </div>
    </>
  );
}

// ---------- bills ----------

function BillLines({ bill }) {
  return (
    <table className="grid inner">
      <tbody>
        {bill.lines.map((l, i) => (
          <tr key={i}><td>{l.date}</td><td>{l.description}</td><td>{l.accountCode} {l.accountName}</td><td className="num">{money(l.amount)}</td></tr>
        ))}
      </tbody>
    </table>
  );
}

function BillsPage({ notify }) {
  const load = useCallback(() => api('/bills'), []);
  const [bills, refresh] = usePoll(load);
  const [tab, setTab] = useState('draft');
  const [dates, setDates] = useState({});
  const [open, setOpen] = useState({});
  const b = bills || { draft: [], approved: [], paid: [] };
  const list = b[tab];

  const approve = async (bill) => {
    const paymentDate = dates[bill.billId];
    if (!paymentDate) return notify(`Pick a payment date for ${bill.billId}.`, true);
    try {
      await api(`/bills/${bill.billId}/approve`, { method: 'POST', body: JSON.stringify({ paymentDate }) });
      notify(`Bill ${bill.billId} approved for payment on ${paymentDate}.`);
      refresh();
    } catch (err) {
      notify(err.message, true);
    }
  };

  return (
    <div className="card">
      <div className="tabs">
        {['draft', 'approved', 'paid'].map((t) => (
          <button key={t} className={tab === t ? 'tab active' : 'tab'} onClick={() => setTab(t)}>
            {t[0].toUpperCase() + t.slice(1)} <span className="count">{b[t].length}</span>
          </button>
        ))}
      </div>
      {list.length === 0 ? <p className="muted">No {tab} bills.</p> : (
        <table className="grid">
          <thead>
            <tr>
              <th>Bill</th><th>Payable to</th><th>Reference</th><th className="num">Total</th>
              {tab === 'draft' && <><th>Created</th><th>Payment date</th><th /></>}
              {tab === 'approved' && <><th>Approved</th><th>Payment date</th></>}
              {tab === 'paid' && <><th>Payment date</th><th>Payment file</th></>}
              <th>PDF</th>
            </tr>
          </thead>
          <tbody>
            {list.map((bill) => (
              <React.Fragment key={bill.billId}>
                <tr data-testid={`bill-${bill.billId}`}>
                  <td><button className="link" onClick={() => setOpen({ ...open, [bill.billId]: !open[bill.billId] })}>{open[bill.billId] ? '▾' : '▸'} {bill.billId}</button></td>
                  <td>{bill.contact?.name}</td>
                  <td>{bill.reference}</td>
                  <td className="num">{money(bill.total)}</td>
                  {tab === 'draft' && (
                    <>
                      <td>{when(bill.createdAt)}</td>
                      <td><input type="date" value={dates[bill.billId] || ''} min={today()} onChange={(e) => setDates({ ...dates, [bill.billId]: e.target.value })} /></td>
                      <td><button className="primary" onClick={() => approve(bill)}>Approve</button></td>
                    </>
                  )}
                  {tab === 'approved' && <><td>{when(bill.approvedAt)}</td><td>{bill.paymentDate}</td></>}
                  {tab === 'paid' && <><td>{bill.paymentDate}</td><td>{bill.paymentFile}</td></>}
                  <td><a href={`/api/bills/${bill.billId}/pdf`} target="_blank" rel="noreferrer">Open PDF</a></td>
                </tr>
                {open[bill.billId] && <tr className="detail"><td colSpan={9}><BillLines bill={bill} /></td></tr>}
              </React.Fragment>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

// ---------- payments ----------

function PaymentsPage() {
  const load = useCallback(() => api('/payments'), []);
  const [files] = usePoll(load);
  const [selected, setSelected] = useState(null);
  const [content, setContent] = useState('');
  const list = files || [];
  const current = list.find((f) => f.file === selected);

  const openFile = async (f) => {
    setSelected(f.file);
    setContent(await api(`/payments/${encodeURIComponent(f.file)}`));
  };

  return (
    <>
      <div className="card">
        <h2>Bank payment files</h2>
        <p className="muted small">Created by NeeliRPA in the payments folder. Check each file, then upload it to the bank.</p>
        {list.length === 0 ? <p className="muted">No payment files yet.</p> : (
          <table className="grid">
            <thead><tr><th>File</th><th>Payment date</th><th className="num">Payments</th><th className="num">Bills</th><th className="num">Total</th><th>Left out</th><th>Created</th><th /></tr></thead>
            <tbody>
              {list.map((f) => (
                <tr key={f.file} className={f.file === selected ? 'selected' : ''} data-testid={`payment-${f.file}`}>
                  <td>{f.file}</td>
                  <td>{f.summary?.paymentDate}</td>
                  <td className="num">{f.summary?.lines}</td>
                  <td className="num">{f.summary?.billCount}</td>
                  <td className="num">{money(f.summary?.total)}</td>
                  <td>{f.summary?.excluded?.length ? <span className="badge badge-exception">{f.summary.excluded.length} payee(s)</span> : '—'}</td>
                  <td>{when(f.modified)}</td>
                  <td className="actions">
                    <button onClick={() => openFile(f)}>Open</button>
                    <a className="button" href={`/api/payments/${encodeURIComponent(f.file)}?download=1`}>Download</a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {current && (
        <div className="card">
          <h2>{current.file}</h2>
          {current.summary && <p>{current.summary.message}</p>}
          <pre className="file">{content}</pre>
          {current.summary && (
            <>
              <h3>Payments</h3>
              <table className="grid">
                <thead><tr><th>Payee</th><th>Bills</th><th className="num">Amount</th></tr></thead>
                <tbody>
                  {current.summary.payments.map((p) => (
                    <tr key={p.employeeId}><td>{p.payee}</td><td>{p.bills.map((b) => b.billId).join(', ')}</td><td className="num">{money(p.amount)}</td></tr>
                  ))}
                </tbody>
              </table>
              {current.summary.excluded.length > 0 && (
                <>
                  <h3>Left out of this file</h3>
                  <table className="grid">
                    <thead><tr><th>Payee</th><th>Bills</th><th className="num">Amount</th><th>Reason</th></tr></thead>
                    <tbody>
                      {current.summary.excluded.map((p) => (
                        <tr key={p.employeeId}><td>{p.payee}</td><td>{p.bills.map((b) => b.billId).join(', ')}</td><td className="num">{money(p.amount)}</td><td className="reason">{p.reason}</td></tr>
                      ))}
                    </tbody>
                  </table>
                </>
              )}
            </>
          )}
        </div>
      )}
    </>
  );
}

// ---------- shell ----------

export default function App() {
  const [user, setUser] = useState(() => JSON.parse(localStorage.getItem('financePocUser') || 'null'));
  const [reference, setReference] = useState(null);
  const [page, setPage] = useState('claims');
  const [toast, setToast] = useState(null);
  const [confirmReset, setConfirmReset] = useState(false);

  useEffect(() => { api('/reference').then(setReference).catch((e) => setToast({ text: e.message, error: true })); }, []);
  useEffect(() => {
    if (!toast) return undefined;
    const t = setTimeout(() => setToast(null), 6000);
    return () => clearTimeout(t);
  }, [toast]);

  const notify = (text, error = false) => setToast({ text, error });
  const signIn = (u) => { localStorage.setItem('financePocUser', JSON.stringify(u)); setUser(u); setPage('claims'); };
  const signOut = () => { localStorage.removeItem('financePocUser'); setUser(null); };
  const reset = async () => {
    setConfirmReset(false);
    try {
      const r = await api('/reset', { method: 'POST' });
      notify(r.message);
    } catch (e) {
      notify(e.message, true);
    }
  };

  if (!reference) return <div className="signin"><p className="muted">Loading…</p></div>;
  if (!user) return <SignIn reference={reference} onSignIn={signIn} />;

  const officer = user.role === 'Finance Officer';
  const pages = officer ? [['claims', 'Claims'], ['bills', 'Bills'], ['payments', 'Payments']] : [['claims', 'Claims']];

  return (
    <div className="app">
      <header>
        <div className="brand">Finance Portal <span className="muted">Demo Organisation</span></div>
        <nav>
          {pages.map(([k, label]) => (
            <button key={k} className={page === k ? 'nav active' : 'nav'} onClick={() => setPage(k)}>{label}</button>
          ))}
        </nav>
        <span className="spacer" />
        {officer && (confirmReset ? (
          <span className="confirm">
            Empty every folder and restore the seed claims?
            <button className="danger" onClick={reset}>Yes, reset</button>
            <button onClick={() => setConfirmReset(false)}>Cancel</button>
          </span>
        ) : <button onClick={() => setConfirmReset(true)}>Reset demo data</button>)}
        <span className="user">{officer ? user.role : `${user.name} · ${user.role}`}</span>
        <button className="link light" onClick={signOut}>Sign out</button>
      </header>
      {toast && <div className={toast.error ? 'toast error' : 'toast'}>{toast.text}</div>}
      <main>
        {page === 'claims' && <ClaimsPage user={user} reference={reference} notify={notify} />}
        {page === 'bills' && officer && <BillsPage notify={notify} />}
        {page === 'payments' && officer && <PaymentsPage />}
      </main>
    </div>
  );
}
