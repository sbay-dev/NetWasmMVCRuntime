// 🧬 CephaKit Server — Node.js HTTP/HTTPS server powered by .NET WASM
// Usage: node cepha-server.mjs
// Runs your MVC controllers as an HTTP API server
// HTTPS: auto-detects dev cert from CEPHA_CERT/CEPHA_KEY env vars or ./obj/ folder

import { createServer as createHttpServer } from 'node:http';
import { createServer as createHttpsServer } from 'node:https';
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PORT = parseInt(process.env.PORT || '3001', 10);
const HOST = process.env.HOST || '0.0.0.0';

// ─── Auto-detect HTTPS certificate ──────────────────────────
function findCert() {
    const certEnv = process.env.CEPHA_CERT;
    const keyEnv = process.env.CEPHA_KEY;
    if (certEnv && keyEnv && existsSync(certEnv) && existsSync(keyEnv)) {
        return { cert: readFileSync(certEnv), key: readFileSync(keyEnv) };
    }
    // Try standard locations relative to project
    const searchPaths = [
        [join(__dirname, '..', 'obj', 'cepha-dev.pem'), join(__dirname, '..', 'obj', 'cepha-dev.key')],
        [join(__dirname, '..', 'obj', 'Debug', 'net10.0', 'cepha-dev.pem'), join(__dirname, '..', 'obj', 'Debug', 'net10.0', 'cepha-dev.key')],
    ];
    for (const [c, k] of searchPaths) {
        if (existsSync(c) && existsSync(k)) {
            return { cert: readFileSync(c), key: readFileSync(k) };
        }
    }
    return null;
}

const tlsOptions = findCert();
const protocol = tlsOptions ? 'https' : 'http';

// ─── Resolve fingerprinted dotnet.js ────────────────────────
const frameworkDir = join(__dirname, '_framework');
const dotnetFile = readdirSync(frameworkDir).find(f => f.startsWith('dotnet.') && f.endsWith('.js') && !f.includes('native') && !f.includes('runtime'));
if (!dotnetFile) { console.error('❌ dotnet.js not found in _framework/'); process.exit(1); }

const { dotnet } = await import(`./_framework/${dotnetFile}`);

// ─── Boot .NET WASM Runtime ─────────────────────────────────
// withApplicationEnvironment('Production') prevents .NET from loading the
// debug-only HotReload library initializer which is unavailable in Node.js context.
const { setModuleImports, getAssemblyExports, getConfig, runMainAndExit } = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationEnvironment('Production')
    .create();

// Register JS imports for C# [JSImport]
setModuleImports('main.js', {
    dom: {
        setInnerHTML: () => {},
        setInnerText: () => {},
        getInnerText: () => '',
        setAttribute: () => {},
        addClass: () => {},
        removeClass: () => {},
        show: () => {},
        hide: () => {}
    },
    storage: {
        getItem: () => null,
        setItem: () => {},
        removeItem: () => {}
    },
    navigation: {
        getPath: () => '/',
        pushState: () => {}
    },
    fileOps: {
        downloadFile: () => {}
    },
    signalr: {
        dispatchHubEvent: () => {}
    },
    cephakit: {
        start: () => {} // No-op in server mode
    },
    cephaDb: {
        restore: () => Promise.resolve(''), // No OPFS in Node.js — SQLite writes to disk
        persist: () => Promise.resolve()
    }
});

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
console.log('🧬 Cepha: .NET WASM module loaded');

// ─── Collect route info ─────────────────────────────────────
let routes = [];
try {
    const info = await exports.Cepha.JsExports.FetchRoute('/_routes');
    if (info) routes = JSON.parse(info).routes || [];
} catch {}

// ─── HTTP/HTTPS Server ──────────────────────────────────────
const handler = async (req, res) => {
    const url = new URL(req.url, `${protocol}://${HOST}:${PORT}`);
    const path = url.pathname;
    const method = req.method;

    // CORS + cross-origin isolation headers (required for SharedArrayBuffer / QEMU WASM)
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
    res.setHeader('Cross-Origin-Embedder-Policy', 'credentialless');
    if (method === 'OPTIONS') { res.writeHead(204); res.end(); return; }

    // /_cepha/info endpoint
    if (path === '/_cepha/info') {
        const info = {
            server: 'CephaKit',
            version: '1.0.0',
            nameOrigin: 'Physarum polycephalum',
            runtime: 'WebAssembly (.NET WASM via Node.js)',
            status: 'running',
            startedAt: startTime,
            uptime: (Date.now() - new Date(startTime).getTime()) / 1000,
            routes,
            routeCount: routes.length,
            capabilities: ['mvc-controllers', 'api-controllers', 'razor-view-rendering', 'signalr-hubs', 'ef-core-sqlite']
        };
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(info, null, 2));
        return;
    }

    // /health endpoint
    if (path === '/health') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end('{"status":"healthy"}');
        return;
    }

    // Read request body
    let body = '';
    if (method === 'POST' || method === 'PUT') {
        body = await new Promise(resolve => {
            let data = '';
            req.on('data', chunk => data += chunk);
            req.on('end', () => resolve(data));
        });
    }

    // Route to .NET MVC engine via HandleRequest
    try {
        const headers = JSON.stringify(req.headers);
        const responseJson = await exports.Cepha.JsExports.HandleRequest(method, path, headers, body || null);
        const response = JSON.parse(responseJson);

        console.log(`[CephaKit] ${method} ${path} → ${response.statusCode}`);
        const ct = response.contentType || 'text/html';
        const fullCt = ct.startsWith('text/') && !ct.includes('charset') ? `${ct}; charset=utf-8` : ct;
        res.writeHead(response.statusCode, { 'Content-Type': fullCt });
        res.end(response.body || '');
    } catch (err) {
        console.error(`[CephaKit] Error: ${err.message}`);
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
    }
};

const startTime = new Date().toISOString();
const server = tlsOptions
    ? createHttpsServer(tlsOptions, handler)
    : createHttpServer(handler);

server.listen(PORT, HOST, () => {
    console.log(`   🧬 CephaKit Server v1.0 — Physarum polycephalum`);
    console.log(`   🌐 Listening on ${protocol}://${HOST}:${PORT}`);
    if (tlsOptions) console.log(`   🔒 HTTPS enabled (dev certificate)`);
    console.log(`   📡 Info:    /_cepha/info`);
    console.log(`   💚 Health:  /health`);
    console.log(`   🚀 Ready!`);
});

// Keep the .NET runtime alive
runMainAndExit();
