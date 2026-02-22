// ???????????????????????????????????????????????????????????????
// ?? Cepha Server � main.mjs
//    Node.js host for the Cepha WASM server runtime.
//    Provides: HTTP server, SSE streams, WebSocket (SignalR transport)
// ???????????????????????????????????????????????????????????????

import { dotnet } from './_framework/dotnet.js';
import { createServer } from 'http';
import { URL } from 'url';

// ??? State ???????????????????????????????????????????????????
const sseConnections = new Map();   // connectionId → { res, path }
const wsConnections = new Map();    // connectionId → ws
const hubSseConnections = new Map(); // connectionId → { res, hubName }
const kvStore = new Map();          // simple key-value store
let dotnetExports = null;
let sseIdCounter = 0;

// ??? Boot .NET WASM ??????????????????????????????????????????
const { setModuleImports, getAssemblyExports, getConfig, runMainAndExit } = await dotnet
    .withDiagnosticTracing(false)
    .create();

// ??? Register JS functions callable from C# via [JSImport] ??
setModuleImports('main.mjs', {
    cepha: {
        // SSE: send an event to a connected client
        sseSend: (connectionId, eventName, dataJson) => {
            const conn = sseConnections.get(connectionId);
            if (conn && !conn.res.writableEnded) {
                conn.res.write(`event: ${eventName}\ndata: ${dataJson}\n\n`);
            }
        },

        // SSE: close a connection
        sseClose: (connectionId) => {
            const conn = sseConnections.get(connectionId);
            if (conn && !conn.res.writableEnded) {
                conn.res.end();
            }
            sseConnections.delete(connectionId);
        },

        // SignalR: dispatch hub event to WebSocket + SSE clients
        dispatchHubEvent: (hubName, method, connectionId, argsJson) => {
            const args = JSON.parse(argsJson || '[]');
            const payload = JSON.stringify({ hubName, method, arguments: args });

            if (connectionId) {
                // Targeted send via WS
                const ws = wsConnections.get(connectionId);
                if (ws && ws.readyState === 1) ws.send(payload);
                // Targeted send via SSE
                const sse = hubSseConnections.get(connectionId);
                if (sse && !sse.res.writableEnded) {
                    sse.res.write(`event: hubEvent\ndata: ${payload}\n\n`);
                }
            } else {
                // Broadcast via WS
                for (const [, ws] of wsConnections) {
                    if (ws.readyState === 1) ws.send(payload);
                }
                // Broadcast via hub SSE
                for (const [, conn] of hubSseConnections) {
                    if (conn.hubName.toLowerCase() === hubName.toLowerCase() && !conn.res.writableEnded) {
                        conn.res.write(`event: hubEvent\ndata: ${payload}\n\n`);
                    }
                }
            }
        },

        // HTTP response (fire-and-forget style, used by SSE initial response)
        sendResponse: (requestId, statusCode, contentType, body) => {
            // Handled inline in the HTTP handler; this is a no-op stub
        },

        sendResponseWithHeaders: (requestId, statusCode, contentType, body, headersJson) => {
            // Handled inline in the HTTP handler; this is a no-op stub
        },

        // Key-value storage
        storageGet: (key) => kvStore.get(key) ?? null,
        storageSet: (key, value) => kvStore.set(key, value),
        storageRemove: (key) => kvStore.delete(key)
    }
});

// ??? Load .NET exports ???????????????????????????????????????
const config = getConfig();
dotnetExports = await getAssemblyExports(config.mainAssemblyName);

console.log('?? Cepha: .NET WASM module loaded');
console.log(`   ${dotnetExports.CephaEntry.Greeting()}`);

// ??? HTTP Server ?????????????????????????????????????????????
const PORT = parseInt(process.env.PORT || '3000', 10);
const HOST = process.env.HOST || '0.0.0.0';

const server = createServer(async (req, res) => {
    const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
    const path = url.pathname + url.search;
    const method = req.method.toUpperCase();

    // 🌐 CORS preflight
    if (method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, PATCH, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type, Authorization',
            'Access-Control-Max-Age': '86400'
        });
        res.end();
        return;
    }

    // ?? SSE endpoint (/sse/*) ????????????????????????????????
    if (path.startsWith('/sse/') && method === 'GET') {
        const connectionId = `sse_${++sseIdCounter}`;
        const targetPath = path.slice(4); // strip /sse prefix

        res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
            'Access-Control-Allow-Origin': '*',
            'X-Powered-By': 'Cepha/1.0'
        });
        res.write(`event: connected\ndata: ${JSON.stringify({ connectionId })}\n\n`);

        sseConnections.set(connectionId, { res, path: targetPath });

        // Notify .NET
        try {
            await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.SseConnect(connectionId, targetPath);
        } catch (e) {
            console.error(`[Cepha] SSE connect error: ${e.message}`);
        }

        req.on('close', async () => {
            sseConnections.delete(connectionId);
            try {
                await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.SseDisconnect(connectionId);
            } catch (e) {
                console.error(`[Cepha] SSE disconnect error: ${e.message}`);
            }
        });

        return; // keep connection open
    }

    // ?? SignalR negotiate endpoint ???????????????????????????
    if (path.startsWith('/hub/') && path.endsWith('/negotiate') && method === 'POST') {
        const hubName = path.split('/')[2];
        try {
            const connectionId = await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.HubConnect(hubName);
            res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
            res.end(JSON.stringify({
                connectionId,
                availableTransports: [{ transport: 'WebSockets' }, { transport: 'ServerSentEvents' }]
            }));
        } catch (e) {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: e.message }));
        }
        return;
    }

    // 🔌 SignalR SSE stream — browser clients receive hub events here
    if (path.startsWith('/hub/') && path.includes('/stream') && method === 'GET') {
        const hubName = path.split('/')[2];
        const connectionId = url.searchParams.get('connectionId');

        res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
            'Access-Control-Allow-Origin': '*'
        });
        res.write(`event: connected\ndata: ${JSON.stringify({ connectionId, hubName })}\n\n`);

        hubSseConnections.set(connectionId, { res, hubName });

        req.on('close', async () => {
            hubSseConnections.delete(connectionId);
            try {
                await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.HubDisconnect(hubName, connectionId);
            } catch (e) { /* ignore */ }
        });

        return; // keep connection open
    }

    // 🔌 SignalR disconnect endpoint
    if (path.startsWith('/hub/') && path.endsWith('/disconnect') && method === 'POST') {
        const hubName = path.split('/')[2];
        const body = await readBody(req);
        try {
            const { connectionId } = JSON.parse(body);
            hubSseConnections.delete(connectionId);
            await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.HubDisconnect(hubName, connectionId);
            res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
            res.end(JSON.stringify({ ok: true }));
        } catch (e) {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: e.message }));
        }
        return;
    }

    // ?? SignalR invoke endpoint (HTTP fallback) ??????????????
    if (path.startsWith('/hub/') && path.endsWith('/invoke') && method === 'POST') {
        const hubName = path.split('/')[2];
        const body = await readBody(req);
        try {
            const { method: hubMethod, connectionId, arguments: args } = JSON.parse(body);
            const result = await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.HubInvoke(
                hubName, hubMethod, connectionId, JSON.stringify(args || [])
            );
            res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
            res.end(result || 'null');
        } catch (e) {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: e.message }));
        }
        return;
    }

    // ?? Server info endpoint ?????????????????????????????????
    if (path === '/_cepha/info' && method === 'GET') {
        try {
            const info = await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.GetServerInfo();
            res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
            res.end(info);
        } catch (e) {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: e.message }));
        }
        return;
    }

    // ?? General HTTP ? .NET MVC pipeline ?????????????????????
    const requestId = `req_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
    const headersJson = JSON.stringify(Object.fromEntries(
        Object.entries(req.headers).map(([k, v]) => [k, Array.isArray(v) ? v.join(', ') : v])
    ));
    const bodyContent = (method === 'POST' || method === 'PUT' || method === 'PATCH')
        ? await readBody(req)
        : null;

    try {
        const responseJson = await dotnetExports.WasmMvcRuntime.Cepha.CephaExports.HandleRequest(
            requestId, method, path, headersJson, bodyContent
        );

        const response = JSON.parse(responseJson);
        const outHeaders = {
            'Content-Type': response.contentType || 'text/plain',
            'Access-Control-Allow-Origin': '*'
        };

        if (response.headers) {
            for (const [k, v] of Object.entries(response.headers)) {
                outHeaders[k] = v;
            }
        }

        res.writeHead(response.statusCode || 200, outHeaders);
        res.end(response.body || '');
    } catch (e) {
        console.error(`[Cepha] Request error: ${e.message}`);
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Internal Server Error', message: e.message }));
    }
});

// ??? Start listening ?????????????????????????????????????????
server.listen(PORT, HOST, () => {
    console.log('');
    console.log('???????????????????????????????????????????????');
    console.log('  ?? Cepha Server � Physarum polycephalum');
    console.log(`  ?? Listening on http://${HOST}:${PORT}`);
    console.log('  ?? SSE:     /sse/{controller}/{action}');
    console.log('  ?? SignalR: /hub/{hubName}/negotiate');
    console.log('  ??  Health:  /health');
    console.log('  ??  Info:    /_cepha/info');
    console.log('???????????????????????????????????????????????');
    console.log('');
});

// ??? Run the .NET Main() (blocks forever via Task.Delay) ?????
runMainAndExit();

// ??? Helpers ?????????????????????????????????????????????????
function readBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        req.on('data', (chunk) => chunks.push(chunk));
        req.on('end', () => resolve(Buffer.concat(chunks).toString('utf-8')));
        req.on('error', reject);
    });
}

