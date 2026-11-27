'use strict';

const express = require('express');
const http    = require('http');
const path    = require('path');

const app  = express();
const PORT = process.env.UI_PORT        || 4000;
const ORCH = process.env.ORCHESTRATOR_URL || 'http://localhost:5080';
const orchUrl = new URL(ORCH);

const COOKIE = 'tracto_session';
const SESSION_MS = 8 * 60 * 60 * 1000;

app.use(express.json({ limit: '10mb' }));

// ── Session ───────────────────────────────────────────────────────────────────
// The orchestrator signs the user in and returns a session token. It is kept in a cookie the
// page's own scripts cannot read (HttpOnly) and is never sent to another site (SameSite=Strict).
// This server adds it as a bearer token to every call it passes on.
function tokenOf(req) {
    const raw = req.headers.cookie || '';
    for (const part of raw.split(';')) {
        const i = part.indexOf('=');
        if (i > 0 && part.slice(0, i).trim() === COOKIE) return decodeURIComponent(part.slice(i + 1).trim());
    }
    return null;
}

function setSession(req, res, token) {
    res.cookie(COOKIE, token, {
        httpOnly: true, sameSite: 'strict', secure: req.secure, path: '/', maxAge: SESSION_MS,
    });
}

function clearSession(res) {
    res.clearCookie(COOKIE, { path: '/' });
}

// One call to the orchestrator, returned as { status, body }.
function orchCall(method, urlPath, body, token) {
    return new Promise((resolve, reject) => {
        const data = body === undefined ? null : JSON.stringify(body);
        const headers = { accept: 'application/json' };
        if (data !== null) {
            headers['content-type'] = 'application/json';
            headers['content-length'] = Buffer.byteLength(data);
        }
        if (token) headers.authorization = 'Bearer ' + token;
        const req = http.request({
            hostname: orchUrl.hostname,
            port: parseInt(orchUrl.port || (orchUrl.protocol === 'https:' ? 443 : 80), 10),
            path: urlPath, method, headers,
        }, res => {
            const chunks = [];
            res.on('data', c => chunks.push(c));
            res.on('end', () => {
                let parsed = {};
                try { parsed = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}'); } catch { /* not JSON */ }
                resolve({ status: res.statusCode, body: parsed });
            });
        });
        req.on('error', reject);
        if (data !== null) req.write(data);
        req.end();
    });
}

function unreachable(res, err) {
    console.error('[orchestrator unreachable]', err.message);
    res.status(502).json({ error: 'PROXY_ERROR', message: 'Cannot reach Orchestrator: ' + err.message });
}

// ── Gate ──────────────────────────────────────────────────────────────────────
// Only the login page, its script and the style sheet are open. Everything else needs a session:
// a page request is sent to the login page, a call gets 401.
const OPEN = new Set(['/login.html', '/login.js', '/style.css', '/favicon.ico']);
app.use((req, res, next) => {
    if (OPEN.has(req.path) || req.path === '/ui-auth/login') return next();
    if (tokenOf(req)) return next();
    if (req.path.startsWith('/api/') || req.path.startsWith('/ui-auth/')) {
        return res.status(401).json({ error: 'AUTH_REQUIRED', message: 'Sign in first' });
    }
    return res.redirect('/login.html');
});

// ── Sign in, sign out, password ───────────────────────────────────────────────
app.post('/ui-auth/login', async (req, res) => {
    try {
        const r = await orchCall('POST', '/auth/login', {
            username: (req.body || {}).username, password: (req.body || {}).password,
        });
        if (r.status !== 200) return res.status(r.status).json({ error: r.body.error, message: r.body.message });
        setSession(req, res, r.body.token);
        const { token, ...user } = r.body; // the token stays in the cookie
        res.json(user);
    } catch (err) { unreachable(res, err); }
});

app.post('/ui-auth/logout', async (req, res) => {
    try { await orchCall('POST', '/auth/logout', undefined, tokenOf(req)); } catch { /* signing out anyway */ }
    clearSession(res);
    res.status(204).end();
});

app.post('/ui-auth/password', async (req, res) => {
    try {
        const r = await orchCall('POST', '/auth/password', {
            currentPassword: (req.body || {}).currentPassword, newPassword: (req.body || {}).newPassword,
        }, tokenOf(req));
        if (r.status !== 200) return res.status(r.status).json({ error: r.body.error, message: r.body.message });
        setSession(req, res, r.body.token); // this session stays signed in; the others end
        res.json({ ok: true });
    } catch (err) { unreachable(res, err); }
});

// ── API proxy ─────────────────────────────────────────────────────────────────
// Forwards /api/* → Orchestrator, stripping the /api prefix, with the user's session.
// The sign-in and password calls go through /ui-auth above, never through here.
app.all('/api/*', (req, res) => {
    const targetPath = req.url.replace(/^\/api/, '') || '/';
    if (/^\/auth\/(login|password)(\?|$)/.test(targetPath)) {
        return res.status(404).json({ error: 'NOT_FOUND', message: 'Use the sign-in and password pages' });
    }

    const options = {
        hostname : orchUrl.hostname,
        port     : parseInt(orchUrl.port || (orchUrl.protocol === 'https:' ? 443 : 80), 10),
        path     : targetPath,
        method   : req.method,
        headers  : {
            'content-type'  : req.headers['content-type'] || 'application/json',
            'accept'        : 'application/json',
            'authorization' : 'Bearer ' + tokenOf(req),
        },
    };

    const proxyReq = http.request(options, proxyRes => {
        // A session the orchestrator no longer accepts is dropped, so the page returns to sign-in.
        if (proxyRes.statusCode === 401) clearSession(res);
        res.status(proxyRes.statusCode);
        res.set('Content-Type', proxyRes.headers['content-type'] || 'application/json');
        if (proxyRes.headers['cache-control']) res.set('Cache-Control', proxyRes.headers['cache-control']);
        proxyRes.pipe(res, { end: true });
    });

    proxyReq.on('error', err => {
        console.error('[proxy error]', err.message);
        res.status(502).json({ error: 'PROXY_ERROR', message: 'Cannot reach Orchestrator: ' + err.message });
    });

    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(req.method) && req.body) {
        const body = JSON.stringify(req.body);
        proxyReq.setHeader('content-length', Buffer.byteLength(body));
        proxyReq.write(body);
        proxyReq.end();
    } else {
        proxyReq.end();
    }
});

// ── Static files ──────────────────────────────────────────────────────────────
app.use(express.static(path.join(__dirname, 'public')));

// SPA fallback
app.get('*', (_req, res) => {
    res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

// ── Start ─────────────────────────────────────────────────────────────────────
app.listen(PORT, () => {
    console.log(`Tracto Orchestrator UI  →  http://localhost:${PORT}`);
    console.log(`Proxying /api/*         →  ${ORCH}`);
});
