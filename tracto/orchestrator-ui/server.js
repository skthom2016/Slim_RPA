'use strict';

const express = require('express');
const http    = require('http');
const path    = require('path');

const app  = express();
const PORT = process.env.UI_PORT        || 4000;
const ORCH = process.env.ORCHESTRATOR_URL || 'http://localhost:5080';
const orchUrl = new URL(ORCH);

app.use(express.json());

// ── API proxy ─────────────────────────────────────────────────────────────────
// Forwards /api/* → Orchestrator, stripping the /api prefix.
// This avoids any CORS issues when the UI is deployed separately.
app.all('/api/*', (req, res) => {
    const targetPath = req.url.replace(/^\/api/, '') || '/';

    const options = {
        hostname : orchUrl.hostname,
        port     : parseInt(orchUrl.port || (orchUrl.protocol === 'https:' ? 443 : 80), 10),
        path     : targetPath,
        method   : req.method,
        headers  : {
            'content-type' : req.headers['content-type'] || 'application/json',
            'accept'       : 'application/json',
        },
    };

    const proxyReq = http.request(options, proxyRes => {
        res.status(proxyRes.statusCode);
        res.set('Content-Type', proxyRes.headers['content-type'] || 'application/json');
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
