// 🧬 NetWasmMvc.SDK — Main Thread (Display Surface)
// This thread is a BLIND display surface — a video screen.
// The Worker (plasma source) pushes frames via postMessage.
// All intelligence, MVC, EF Core, and SQLite run in the Worker.
// main.js only renders what it's told — it has ZERO page awareness.

const __DEV__ = location.hostname === 'localhost' || location.hostname === '127.0.0.1' || location.hostname === '[::1]';
if (__DEV__) console.log('%c🧬 NetWasmMvc.SDK — Display Surface', 'color: #667eea; font-weight: bold');

// ─── CephaLoader: Native Loading System ──────────────────────

const CephaLoader = (() => {
    // Context-aware messages based on controller/action path
    const messages = {
        '/identity/account/login':    { text: '🔐 Signing in...', sub: 'Verifying credentials' },
        '/identity/account/register': { text: '🧬 Creating your account...', sub: 'Setting up your profile' },
        '/identity/account/logout':   { text: '👋 Signing out...', sub: 'Clearing session' },
        '/identity/account/profile':  { text: '📋 Loading profile...', sub: 'Fetching data' },
    };

    const defaultNav = { text: '🧬 Loading...', sub: 'Cepha' };
    const defaultSubmit = { text: '⚡ Processing...', sub: 'Please wait' };

    let overlay = null;
    let navBar = null;
    let _activeForm = null;

    function init() {
        // Overlay (form submissions)
        overlay = document.createElement('div');
        overlay.className = 'cepha-loader-overlay';
        overlay.innerHTML = `
            <div class="cepha-loader-sigil">
                <span class="cepha-loader-node"></span>
                <span class="cepha-loader-node"></span>
                <span class="cepha-loader-node"></span>
                <span class="cepha-loader-node"></span>
                <div class="cepha-loader-veins"></div>
            </div>
            <div class="cepha-loader-message"></div>
            <div class="cepha-loader-sub"></div>`;
        document.body.appendChild(overlay);

        // Nav progress bar (link navigation)
        navBar = document.createElement('div');
        navBar.className = 'cepha-nav-progress';
        document.body.appendChild(navBar);
    }

    function getMessage(action, isSubmit) {
        const key = action.toLowerCase().replace(/\/$/, '');
        if (messages[key]) return messages[key];

        // Auto-detect from path segments
        const parts = key.split('/').filter(Boolean);
        const verb = parts[parts.length - 1] || '';
        const area = parts[0] || '';

        if (isSubmit) {
            if (verb === 'login' || verb === 'signin') return { text: '🔐 Signing in...', sub: 'Verifying credentials' };
            if (verb === 'register' || verb === 'signup') return { text: '🧬 Creating account...', sub: 'Setting up' };
            if (verb === 'delete' || verb === 'remove') return { text: '🗑️ Removing...', sub: 'Processing' };
            if (verb === 'save' || verb === 'update' || verb === 'edit') return { text: '💾 Saving...', sub: 'Updating data' };
            if (verb === 'search' || verb === 'find') return { text: '🔍 Searching...', sub: 'Querying' };
            if (verb === 'send' || verb === 'submit') return { text: '📤 Sending...', sub: 'Processing' };
            return defaultSubmit;
        }
        return defaultNav;
    }

    function showOverlay(action, isSubmit = true) {
        if (!overlay) init();
        const msg = getMessage(action, isSubmit);
        overlay.querySelector('.cepha-loader-message').textContent = msg.text;
        overlay.querySelector('.cepha-loader-sub').textContent = msg.sub;
        overlay.classList.add('active');
    }

    function hideOverlay() {
        if (overlay) overlay.classList.remove('active');
        releaseForm();
    }

    function lockForm(form) {
        if (!form) return;
        _activeForm = form;
        form.classList.add('cepha-processing');
    }

    function releaseForm() {
        if (_activeForm) {
            _activeForm.classList.remove('cepha-processing');
            _activeForm = null;
        }
    }

    function startNav() {
        if (!navBar) init();
        navBar.classList.remove('done');
        navBar.classList.add('active');
    }

    function endNav() {
        if (!navBar) return;
        navBar.classList.remove('active');
        navBar.classList.add('done');
        setTimeout(() => navBar.classList.remove('done'), 500);
    }

    return { init, showOverlay, hideOverlay, lockForm, releaseForm, startNav, endNav };
})();

// Init loader DOM elements
CephaLoader.init();

// ─── Boot Runtime Worker (.NET runs here) ────────────────────

const worker = new Worker('./cepha-runtime-worker.js', { type: 'module' });

// Wait for worker to finish dotnet.create()
await new Promise(resolve => {
    const h = (e) => {
        if (e.data.type === 'created') { worker.removeEventListener('message', h); resolve(); }
    };
    worker.addEventListener('message', h);
});

// Send init: current path + localStorage snapshot
const storageSnapshot = {};
for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i);
    if (k) storageSnapshot[k] = localStorage.getItem(k);
}
worker.postMessage({ type: 'init', path: location.pathname || '/', storage: storageSnapshot, fingerprint: navigator.userAgent });

// ─── CephaKit Client (runs on main thread — needs fetch + DOM) ──

window.CephaClient = {
    serverUrl: null,
    connected: false,
    async discover(urls) {
        const candidates = urls || [
            `${location.protocol}//${location.hostname}:3000`,
            'http://localhost:3000'
        ];
        for (const url of candidates) {
            try {
                const res = await fetch(`${url}/_cepha/info`, { signal: AbortSignal.timeout(2000) });
                if (res.ok) {
                    this.serverUrl = url;
                    this.connected = true;
                    if (__DEV__) console.log(`%c🧬 Cepha server: ${url}`, 'color: #28a745; font-weight: bold');
                    return await res.json();
                }
            } catch { /* next */ }
        }
        if (__DEV__) console.log('%c🧬 Cepha: offline mode (Worker-only)', 'color: #ffc107');
        return null;
    },
    async fetch(method, path, body) {
        if (!this.connected) return null;
        const opts = { method, headers: { 'Content-Type': 'application/json' } };
        if (body && ['POST', 'PUT', 'PATCH'].includes(method))
            opts.body = typeof body === 'string' ? body : JSON.stringify(body);
        return await (await fetch(`${this.serverUrl}${path}`, opts)).json();
    }
};

// ─── Cross-Tab Auth Sync (BroadcastChannel) ─────────────────

const _authChannel = new BroadcastChannel('cepha-auth');
let _authSyncTimer = null;
_authChannel.onmessage = (e) => {
    // Debounce: if multiple auth-sync messages arrive quickly, process only the last
    clearTimeout(_authSyncTimer);
    _authSyncTimer = setTimeout(() => {
        worker.postMessage({ type: 'auth-sync', path: location.pathname });
    }, 50);
};

// ─── Frame Buffer + Render Loop (Video Stream Architecture) ──
// The main thread is a blind display surface in a permanent rAF loop.
// The Worker is the plasma source — it pushes frames, we just display.

const _frameBuffer = [];
let _frameSeq = 0;
let _rafScheduled = false;

function pushFrame(frame) {
    frame._seq = ++_frameSeq;
    _frameBuffer.push(frame);
    if (!_rafScheduled) {
        _rafScheduled = true;
        requestAnimationFrame(renderLoop);
    }
}

function renderLoop() {
    // Drain all queued frames in sequence (one rAF pass)
    while (_frameBuffer.length > 0) {
        applyFrame(_frameBuffer.shift());
    }
    _rafScheduled = false;
}

function applyFrame(frame) {
    const el = document.querySelector(frame.selector);
    if (!el) return;

    // Suppress transitions during DOM swap to prevent theme flicker
    const root = document.documentElement;
    root.classList.add('cepha-no-transition');

    switch (frame.op) {
        case 'setInnerHTML':
            // Resolve ~/ prefix in attribute values (fallback — C# PostProcessHtml handles this too)
            let html = frame.value.replace(/(href|src|action)\s*=\s*"~\//g, '$1="/');
            // Strip scoped CSS isolation links (*.styles.css) — not generated in WASM mode
            html = html.replace(/<link[^>]*href="[^"]*\.styles\.css[^"]*"[^>]*\/?>/gi, '');
            el.innerHTML = html;
            // Execute <script> tags — innerHTML doesn't run them natively
            activateScripts(el);
            CephaLoader.hideOverlay();
            CephaLoader.endNav();
            break;
        case 'setInnerText':  el.innerText = frame.value; break;
        case 'setAttribute':  el.setAttribute(frame.attr, frame.value); break;
        case 'addClass':      el.classList.add(frame.value); break;
        case 'removeClass':   el.classList.remove(frame.value); break;
        case 'show':          el.style.display = ''; break;
        case 'hide':          el.style.display = 'none'; break;
        case 'streamStart':
            el.innerHTML = '';
            break;
        case 'streamAppend':
            el.insertAdjacentHTML('beforeend', frame.value);
            break;
        case 'streamEnd':
            activateScripts(el);
            CephaLoader.hideOverlay();
            CephaLoader.endNav();
            break;
    }

    // Re-enable transitions after paint (GPU-composited, no flicker)
    requestAnimationFrame(() => {
        requestAnimationFrame(() => root.classList.remove('cepha-no-transition'));
    });
}

// Activate resources from innerHTML (which doesn't run scripts or load stylesheets natively).
// Mimics default browser page-load behaviour:
//   1. Promote <link rel="stylesheet"> to <head> and wait for them to load (render-blocking, like a real <head>).
//   2. Promote <style> tags to <head> for reliable application.
//   3. Process <script> tags in document order — external scripts wait for
//      onload before the next script runs, inline scripts execute immediately.
function activateScripts(container) {
    // ── CSS: promote <style> tags to <head> ──
    document.querySelectorAll('style[data-cepha-view]').forEach(s => s.remove());
    for (const s of [...container.querySelectorAll('style')]) {
        s.setAttribute('data-cepha-view', '');
        s.remove();
        document.head.appendChild(s);
    }
    // ── CSS: promote <link rel="stylesheet"> to <head> and wait ──
    document.querySelectorAll('link[data-cepha-view]').forEach(l => l.remove());
    const cssLinks = [...container.querySelectorAll('link[rel="stylesheet"]')];
    let cssRemaining = cssLinks.length;

    function runScripts() {
        const scripts = [...container.querySelectorAll('script')];
        let i = 0;
        function next() {
            if (i >= scripts.length) return;
            const old = scripts[i++];
            const live = document.createElement('script');
            for (const attr of old.attributes) {
                let val = attr.value;
                // Resolve ~/ prefix in src attribute
                if (attr.name === 'src' && val.startsWith('~/')) val = val.substring(1);
                live.setAttribute(attr.name, val);
            }
            if (!live.src && old.textContent.trim()) {
                // Replace let/const with var for top-level declarations to avoid
                // redeclaration errors on SPA re-renders, while keeping functions global
                live.textContent = old.textContent
                    .replace(/^(\s*)(let|const)\s+/gm, '$1var ');
            } else {
                live.textContent = old.textContent;
            }
            if (live.src) live.onload = live.onerror = next;
            old.parentNode.replaceChild(live, old);
            if (!live.src) next();
        }
        next();
    }

    if (cssRemaining === 0) { runScripts(); return; }

    for (const l of cssLinks) {
        const live = document.createElement('link');
        for (const attr of l.attributes) {
            let val = attr.value;
            // Resolve ~/ prefix in href attribute
            if (attr.name === 'href' && val.startsWith('~/')) val = val.substring(1);
            live.setAttribute(attr.name, val);
        }
        live.setAttribute('data-cepha-view', '');
        l.remove();
        live.onload = live.onerror = () => { if (--cssRemaining === 0) runScripts(); };
        document.head.appendChild(live);
    }
}

// ─── Handle Worker Messages (render HTML results) ────────────

let _hubMsgId = 0;
const _hubPending = new Map();

worker.onmessage = (e) => {
    const d = e.data;
    switch (d.type) {
        // ── DOM rendering → Frame Buffer (display surface) ───
        case 'dom':
            pushFrame(d);
            break;
        // ── Navigation ───────────────────────────────────
        case 'pushState':
            history.pushState({}, '', d.path);
            break;

        // ── localStorage sync ────────────────────────────
        case 'storage':
            if (d.op === 'set') localStorage.setItem(d.key, d.value);
            else if (d.op === 'remove') localStorage.removeItem(d.key);
            break;

        // ── CephaKit discovery (delegated from worker) ───
        case 'cephakit':
            window.CephaClient.discover([
                `https://${location.hostname}:${d.port}`,
                `http://${location.hostname}:${d.port}`
            ]);
            break;

        // ── Cross-tab auth broadcast ─────────────────────
        case 'auth-changed':
            _authChannel.postMessage({ action: d.action, ts: Date.now() });
            break;

        // ── OPFS DB bridge (worker ↔ OPFS data worker) ──
        case 'cephaDb':
            if (d.op === 'restore') {
                window.CephaData.importDb().then(result => {
                    let base64 = '';
                    if (result?.data && result.data.byteLength > 100) {
                        const bytes = new Uint8Array(result.data);
                        const chunks = [];
                        for (let i = 0; i < bytes.length; i += 8192)
                            chunks.push(String.fromCharCode.apply(null, bytes.subarray(i, i + 8192)));
                        base64 = btoa(chunks.join(''));
                    }
                    worker.postMessage({ type: 'cephaDb-result', id: d.id, result: base64 });
                }).catch(() => {
                    worker.postMessage({ type: 'cephaDb-result', id: d.id, result: '' });
                });
            } else if (d.op === 'persist') {
                const binary = atob(d.base64);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                window.CephaData.exportDb(bytes.buffer).then(() => {
                    worker.postMessage({ type: 'cephaDb-result', id: d.id });
                }).catch(() => {
                    worker.postMessage({ type: 'cephaDb-result', id: d.id });
                });
            }
            break;

        // ── OPFS generic read/write (worker ↔ OPFS data worker) ──
        case 'opfs':
            if (d.op === 'write') {
                window.CephaData.write(d.path, d.data).then(() => {
                    worker.postMessage({ type: 'opfs-result', id: d.id, result: true });
                }).catch(() => {
                    worker.postMessage({ type: 'opfs-result', id: d.id, result: false });
                });
            } else if (d.op === 'read') {
                window.CephaData.read(d.path, true).then(data => {
                    worker.postMessage({ type: 'opfs-result', id: d.id, result: data ?? null });
                }).catch(() => {
                    worker.postMessage({ type: 'opfs-result', id: d.id, result: null });
                });
            }
            break;

        // ── File download ────────────────────────────────
        case 'download': {
            const bin = atob(d.b64);
            const arr = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
            const blob = new Blob([arr], { type: d.mime });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = d.name;
            a.click();
            URL.revokeObjectURL(a.href);
            break;
        }

        // ── SignalR events → cross-tab broadcast ─────────
        case 'signalr': {
            const key = `${d.hubName}:${d.method}`.toLowerCase();
            const handlers = window.__signalrHandlers?.[key] || [];
            const args = JSON.parse(d.argsJson || '[]');
            handlers.forEach(fn => { try { fn(...args); } catch {} });
            window.__signalrChannel?.postMessage({
                hubName: d.hubName, method: d.method, connectionId: d.connectionId, argsJson: d.argsJson
            });
            break;
        }

        // ── SignalR hub operation results ─────────────────
        case 'hub-result': {
            const resolver = _hubPending.get(d.id);
            if (resolver) { _hubPending.delete(d.id); resolver(d.result); }
            break;
        }

        // ── Fetch API results ────────────────────────────
        case 'fetch-result': {
            const fetchResolver = _fetchPending.get(d.id);
            if (fetchResolver) { _fetchPending.delete(d.id); fetchResolver(d.response); }
            break;
        }
    }
};

// ─── Fetch Intercept: route API calls through worker ─────────

let _fetchMsgId = 0;
const _fetchPending = new Map();
const _originalFetch = window.fetch.bind(window);

window.fetch = function(input, init) {
    const url = typeof input === 'string' ? input : input?.url || '';
    // Only intercept same-origin relative paths that look like API/controller routes
    if (url.startsWith('/') && !url.startsWith('//') && !/\.\w{2,5}(\?|$)/.test(url)) {
        const method = (init?.method || 'GET').toUpperCase();
        let body = null;
        if (init?.body) {
            body = typeof init.body === 'string' ? init.body : JSON.stringify(init.body);
        }
        return new Promise((resolve) => {
            const id = ++_fetchMsgId;
            _fetchPending.set(id, (responseJson) => {
                try {
                    const parsed = JSON.parse(responseJson);
                    const resp = new Response(parsed.body || '', {
                        status: parsed.statusCode || 200,
                        headers: { 'Content-Type': parsed.contentType || 'application/json' }
                    });
                    resolve(resp);
                } catch {
                    resolve(new Response('{}', { status: 500 }));
                }
            });
            worker.postMessage({ type: 'fetch', id, method, path: url, body });
        });
    }
    return _originalFetch(input, init);
};

// ─── EventSource Intercept: route SSE through worker fetch ───
// EventSource doesn't use fetch, so the override above won't catch it.
// For same-origin API paths, we fetch log data through the worker and
// emit events through a synthetic EventSource — proper abstraction.

const _OriginalEventSource = window.EventSource;
window.EventSource = function(url, opts) {
    if (typeof url === 'string' && url.startsWith('/') && !url.startsWith('//') && !/\.\w{2,5}(\?|$)/.test(url)) {
        const fake = new EventTarget();
        fake.url = url;
        fake.readyState = 0; // CONNECTING
        fake.close = () => { fake.readyState = 2; };
        fake.onopen = null;
        fake.onmessage = null;
        fake.onerror = null;

        // Fetch log data through worker, then emit as SSE events
        (async () => {
            try {
                const id = ++_fetchMsgId;
                const responseJson = await new Promise(resolve => {
                    _fetchPending.set(id, resolve);
                    worker.postMessage({ type: 'fetch', id, method: 'GET', path: url, body: null });
                });
                const parsed = JSON.parse(responseJson);
                let lines = [];
                try { lines = JSON.parse(parsed.body || '[]'); } catch { lines = []; }
                if (!Array.isArray(lines)) lines = [];

                fake.readyState = 1; // OPEN
                if (fake.onopen) fake.onopen(new Event('open'));

                for (let i = 0; i < lines.length; i++) {
                    if (fake.readyState === 2) break;
                    await new Promise(r => setTimeout(r, 60));
                    const evt = new MessageEvent('message', { data: JSON.stringify(lines[i]) });
                    if (fake.onmessage) fake.onmessage(evt);
                    try { fake.dispatchEvent(evt); } catch {}
                }
            } catch (e) {
                if (__DEV__) console.warn('🧬 EventSource stub error:', e);
                if (fake.onerror) fake.onerror(new Event('error'));
            }
        })();

        return fake;
    }
    return new _OriginalEventSource(url, opts);
};
window.EventSource.CONNECTING = 0;
window.EventSource.OPEN = 1;
window.EventSource.CLOSED = 2;

// ─── SPA Router: intercept link clicks ───────────────────────

document.addEventListener('click', (e) => {
    const link = e.target.closest('a[href]');
    if (!link) return;
    const href = link.getAttribute('href');
    if (!href || href.startsWith('http') || href.startsWith('//') || href.startsWith('#')) return;
    if (link.getAttribute('target') === '_blank') return;
    if (/\.\w{2,5}$/.test(href)) return;
    e.preventDefault();
    CephaLoader.startNav();
    history.pushState({}, '', href);
    worker.postMessage({ type: 'navigate', path: href });
});

// ─── SPA Router: handle back/forward ─────────────────────────

addEventListener('popstate', () => {
    CephaLoader.startNav();
    worker.postMessage({ type: 'navigate', path: location.pathname });
});

// ─── Form submission handler ─────────────────────────────────

document.addEventListener('submit', (e) => {
    const form = e.target;
    if (!form || form.tagName !== 'FORM') return;
    e.preventDefault();
    const action = form.getAttribute('action') || location.pathname;
    const data = Object.fromEntries(new FormData(form).entries());

    // Lock form + show loader
    CephaLoader.lockForm(form);
    CephaLoader.showOverlay(action, true);

    // Route through CephaKit server when connected
    if (window.CephaClient?.connected) {
        fetch(`${window.CephaClient.serverUrl}${action}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        })
        .then(r => r.json())
        .then(res => {
            CephaLoader.hideOverlay();
            if (res.statusCode === 302 && res.body) {
                history.pushState({}, '', res.body);
                worker.postMessage({ type: 'navigate', path: res.body });
            } else if (res.body) {
                document.querySelector('#app').innerHTML = res.body;
            }
        })
        .catch(() => {
            worker.postMessage({ type: 'submit', action, data });
        });
        return;
    }

    // Worker handles it (non-blocking — all .NET runs off main thread)
    worker.postMessage({ type: 'submit', action, data });
});

// ─── SignalR Client (proxy to worker) ────────────────────────

window.__signalrHandlers = {};
window.__signalrChannel = new BroadcastChannel('cepha-signalr');
window.__signalrChannel.onmessage = ({ data }) => {
    const key = `${data.hubName}:${data.method}`.toLowerCase();
    const args = JSON.parse(data.argsJson || '[]');
    (window.__signalrHandlers[key] || []).forEach(fn => { try { fn(...args); } catch {} });
};

window.SignalR = {
    connections: {},
    async connect(hubName) {
        const id = ++_hubMsgId;
        const connId = await new Promise(resolve => {
            _hubPending.set(id, resolve);
            worker.postMessage({ type: 'hub-connect', hubName, id });
        });
        this.connections[hubName] = connId;
        return connId;
    },
    async disconnect(hubName) {
        const connId = this.connections[hubName];
        if (connId) {
            worker.postMessage({ type: 'hub-disconnect', hubName, connId });
            delete this.connections[hubName];
        }
    },
    async invoke(hubName, method, ...args) {
        const id = ++_hubMsgId;
        return new Promise(resolve => {
            _hubPending.set(id, resolve);
            worker.postMessage({
                type: 'hub-invoke', hubName, method,
                connId: this.connections[hubName] || '',
                argsJson: JSON.stringify(args), id
            });
        });
    },
    on(hubName, method, callback) {
        const key = `${hubName}:${method}`.toLowerCase();
        if (!window.__signalrHandlers[key]) window.__signalrHandlers[key] = [];
        window.__signalrHandlers[key].push(callback);
    },
    off(hubName, method) {
        delete window.__signalrHandlers[`${hubName}:${method}`.toLowerCase()];
    }
};

// ─── CephaData: OPFS Worker Bridge ──────────────────────────

window.CephaData = (() => {
    let opfsWorker = null;
    let ready = false;
    let msgId = 0;
    const pending = new Map();
    const subscribers = new Map();
    const channel = new BroadcastChannel('cepha-data');

    function init() {
        if (opfsWorker) return;
        try {
            opfsWorker = new Worker('./cepha-data-worker.js');
            opfsWorker.onmessage = ({ data }) => {
                if (data.type === 'ready') {
                    ready = true;
                    send('init').then(() => {
                        if (__DEV__) console.log('%c🧬 CephaData: OPFS worker ready', 'color: #48bb78; font-weight: bold');
                    });
                    return;
                }
                const p = pending.get(data.id);
                if (p) {
                    pending.delete(data.id);
                    data.type === 'error' ? p.reject(new Error(data.error)) : p.resolve(data.result);
                }
            };
            opfsWorker.onerror = (e) => console.error('CephaData worker error:', e.message);
        } catch {
            console.warn('🧬 CephaData: Worker unavailable, OPFS disabled');
        }
    }

    function send(type, payload) {
        return new Promise((resolve, reject) => {
            if (!opfsWorker) { reject(new Error('Worker not initialized')); return; }
            const id = ++msgId;
            pending.set(id, { resolve, reject });
            opfsWorker.postMessage({ id, type, payload });
        });
    }

    channel.onmessage = ({ data }) => {
        (subscribers.get(data.event) || []).forEach(fn => { try { fn(data.detail); } catch {} });
    };

    function notify(event, detail) {
        channel.postMessage({ event, detail });
        (subscribers.get(event) || []).forEach(fn => { try { fn(detail); } catch {} });
    }

    init();

    return {
        write: (path, data) => send('write', { path, data }),
        read: (path, asText) => send('read', { path, asText }),
        delete: (path) => send('delete', { path }),
        list: (path) => send('list', { path }),
        exportDb: (data) => send('db-export', { data }),
        importDb: () => send('db-import'),
        enqueue: (change) => send('enqueue', change).then(count => { notify('pending-change', { count, change }); return count; }),
        dequeue: () => send('dequeue'),
        pendingCount: () => send('pending-count'),
        stats: () => send('stats'),
        on(event, fn) { if (!subscribers.has(event)) subscribers.set(event, []); subscribers.get(event).push(fn); },
        off(event, fn) { const s = subscribers.get(event); if (s) subscribers.set(event, s.filter(f => f !== fn)); },
        notify,
        get isEncrypted() { return false; },
        get isPremium() { return false; }
    };
})();

// ─── WebSocket Intercept: VNC in WASM mode ──────────────────
// In WASM mode there is no real QEMU, so VNC WebSocket connections
// to /nc-vnc/{guestId} are intercepted and served by a synthetic
// RFB server that renders an interactive OpenWrt TTY on the noVNC canvas.

const _OriginalWebSocket = window.WebSocket;

window.WebSocket = function(url, protocols) {
    // Only intercept nc-vnc paths (VNC bridge endpoint)
    if (typeof url === 'string' && url.includes('/nc-vnc/')) {
        const guestIdMatch = url.match(/\/nc-vnc\/([^/?#]+)/);
        const guestId = guestIdMatch ? guestIdMatch[1] : 'unknown';
        if (__DEV__) console.log(`🖥 VNC-TTY: intercepting WebSocket for guest ${guestId.substring(0, 12)}…`);
        return new CephaTtySocket(url, guestId);
    }
    // Pass through all other WebSocket connections
    if (protocols !== undefined) return new _OriginalWebSocket(url, protocols);
    return new _OriginalWebSocket(url);
};
// Preserve WebSocket constants
window.WebSocket.CONNECTING = 0;
window.WebSocket.OPEN = 1;
window.WebSocket.CLOSING = 2;
window.WebSocket.CLOSED = 3;
window.WebSocket.prototype = _OriginalWebSocket.prototype;

// ─── CephaTtySocket: synthetic RFB WebSocket for WASM VNC ───
// Implements just enough of the RFB 3.8 handshake + FramebufferUpdate
// to satisfy noVNC's RFB client, rendering a text-mode TTY.

class CephaTtySocket {
    constructor(url, guestId) {
        this.url = url;
        this._guestId = guestId;
        this.readyState = 0; // CONNECTING
        this.binaryType = 'arraybuffer';
        this.protocol = 'binary';
        this.extensions = '';
        this.bufferedAmount = 0;
        this.onopen = null;
        this.onmessage = null;
        this.onclose = null;
        this.onerror = null;
        this._listeners = {};
        this._phase = 'version'; // version → security → secresult → init → running
        this._closed = false;

        // TTY state: 80x24 text terminal
        this._cols = 80;
        this._rows = 24;
        this._charW = 8;
        this._charH = 16;
        this._width = this._cols * this._charW;   // 640
        this._height = this._rows * this._charH;  // 384
        this._lines = [];
        this._scrollback = [];       // lines that scrolled off top
        this._scrollOffset = 0;      // 0 = bottom (live), >0 = scrolled up
        this._maxScrollback = 500;
        this._cursorX = 0;
        this._cursorY = 0;
        this._inputBuffer = '';
        for (let i = 0; i < this._rows; i++) this._lines.push('');

        // Boot content
        this._bootLines = [
            'BusyBox v1.36.1 (OpenWrt r28427-6df0e3d02a) built-in shell (ash)',
            '',
            '  _______                     ________        __',
            ' |       |.-----.-----.-----.|  |  |  |.----.|  |_',
            ' |   -   ||  _  |  -__|     ||  |  |  ||   _||   _|',
            ' |_______||   __|_____|__|__||________||__|  |____|',
            '          |__| W I R E L E S S   F R E E D O M',
            ' -----------------------------------------------------',
            ' OpenWrt 24.10.5, r28427-6df0e3d02a',
            ' -----------------------------------------------------',
            '',
        ];
        this._promptStr = 'root@OpenWrt:~# ';
        this._booted = false;
        this._cmdHistory = [];
        this._dirty = true;          // first frame must be sent
        this._updateRequested = false; // client hasn't asked yet
        this._frameThrottle = null;  // rAF handle for throttled delivery

        // Start handshake after current sync code completes
        Promise.resolve().then(() => {
            try { this._startHandshake(); }
            catch (e) { console.error('🖥 VNC-TTY: _startHandshake error:', e); }
        });
    }

    addEventListener(type, fn) {
        if (!this._listeners[type]) this._listeners[type] = [];
        this._listeners[type].push(fn);
    }
    removeEventListener(type, fn) {
        const arr = this._listeners[type];
        if (arr) this._listeners[type] = arr.filter(f => f !== fn);
    }
    dispatchEvent(evt) {
        if (this['on' + evt.type]) this['on' + evt.type](evt);
        (this._listeners[evt.type] || []).forEach(fn => fn(evt));
        return true;
    }
    _emit(type, props) {
        const evt = Object.assign({ type }, props || {});
        try {
            if (this['on' + type]) this['on' + type](evt);
            (this._listeners[type] || []).forEach(fn => fn(evt));
        } catch (e) {
            console.error(`🖥 VNC-TTY: _emit(${type}) error:`, e);
        }
    }

    _startHandshake() {
        if (this._closed) return;
        console.log('🖥 VNC-TTY: handshake started');
        this.readyState = 1; // OPEN
        this._emit('open');
        // Send RFB 3.8 version string on next microtask
        Promise.resolve().then(() => {
            if (this._closed) return;
            const ver = new TextEncoder().encode('RFB 003.008\n');
            this._deliverMessage(ver.buffer);
        });
    }

    // Called by noVNC Websock.flush() → _websocket.send()
    send(data) {
        if (this._closed) return;
        // Properly handle Uint8Array views (noVNC sends subarray of sQ buffer)
        let bytes;
        if (data instanceof Uint8Array) {
            bytes = new Uint8Array(data); // copy to avoid stale buffer views
        } else if (data instanceof ArrayBuffer) {
            bytes = new Uint8Array(data);
        } else {
            bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
        }

        try {
            switch (this._phase) {
                case 'version':
                    this._phase = 'security';
                    this._deliverMessage(new Uint8Array([1, 1]).buffer);
                    break;

                case 'security':
                    this._phase = 'secresult';
                    const sr = new ArrayBuffer(4);
                    new DataView(sr).setUint32(0, 0);
                    this._deliverMessage(sr);
                    break;

                case 'secresult':
                    this._phase = 'init';
                    this._sendServerInit();
                    break;

                case 'init':
                case 'running':
                    this._phase = 'running';
                    this._handleClientMessage(bytes);
                    break;
            }
        } catch (e) {
            console.error(`🖥 VNC-TTY: send() error in phase ${this._phase}:`, e);
        }
    }

    // Deliver binary data to noVNC
    _deliverMessage(buffer) {
        if (this._closed) return;
        // Use setTimeout (macrotask) to yield to the event loop between frames
        // This prevents microtask storms that freeze the browser
        setTimeout(() => {
            if (this._closed) return;
            this._emit('message', { data: buffer });
        }, 0);
    }

    _sendServerInit() {
        // ServerInit: width(2) + height(2) + pixel_format(16) + name_length(4) + name
        const name = 'OpenWrt TTY';
        const nameBytes = new TextEncoder().encode(name);
        const buf = new ArrayBuffer(24 + nameBytes.length);
        const dv = new DataView(buf);
        dv.setUint16(0, this._width);   // width  (big-endian default)
        dv.setUint16(2, this._height);  // height
        // Pixel format: 32bpp, depth 24, big-endian=0, true-color=1
        dv.setUint8(4, 32);  // bpp
        dv.setUint8(5, 24);  // depth
        dv.setUint8(6, 0);   // big-endian
        dv.setUint8(7, 1);   // true-color
        dv.setUint16(8, 255);  // red-max
        dv.setUint16(10, 255); // green-max
        dv.setUint16(12, 255); // blue-max
        dv.setUint8(14, 16);  // red-shift
        dv.setUint8(15, 8);   // green-shift
        dv.setUint8(16, 0);   // blue-shift
        // 3 bytes padding (17, 18, 19) — already 0
        dv.setUint32(20, nameBytes.length); // name length (big-endian)
        const u8 = new Uint8Array(buf);
        u8.set(nameBytes, 24);
        console.log(`🖥 VNC-TTY: connected ${this._width}x${this._height}`);
        this._deliverMessage(buf);

        // Boot sequence after handshake settles
        setTimeout(() => this._runBoot(), 200);
    }

    async _runBoot() {
        for (const line of this._bootLines) {
            this._writeLine(line);
            await this._delay(40);
        }
        this._writePrompt();
        this._booted = true;
    }

    _handleClientMessage(bytes) {
        if (bytes.length === 0) return;
        const msgType = bytes[0];

        switch (msgType) {
            case 0: // SetPixelFormat — ignore
                break;
            case 2: // SetEncodings — ignore
                break;
            case 3: // FramebufferUpdateRequest
                // Only send a frame if content has changed (dirty flag)
                if (this._dirty) {
                    this._dirty = false;
                    this._updateRequested = false;
                    this._scheduleFrame();
                } else {
                    this._updateRequested = true; // remember client wants an update
                }
                break;
            case 4: // KeyEvent
                if (bytes.length >= 8) {
                    const downFlag = bytes[1];
                    const keysym = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7];
                    if (downFlag) this._handleKey(keysym);
                }
                break;
            case 5: // PointerEvent — handle scroll wheel
                if (bytes.length >= 6) {
                    const buttonMask = bytes[1];
                    // Button 4 (bit 3) = scroll up, Button 5 (bit 4) = scroll down
                    if (buttonMask & 8) this._scroll(3);   // scroll up 3 lines
                    if (buttonMask & 16) this._scroll(-3);  // scroll down 3 lines
                }
                break;
            case 6: // ClientCutText — ignore
                break;
        }
    }

    // Schedule a frame delivery on next animation frame (throttled)
    _scheduleFrame() {
        if (this._frameThrottle || this._closed) return;
        this._frameThrottle = requestAnimationFrame(() => {
            this._frameThrottle = null;
            if (!this._closed) this._sendFullFrame();
        });
    }

    // Scroll the TTY view through scrollback history
    _scroll(delta) {
        const maxOffset = this._scrollback.length;
        const newOffset = Math.max(0, Math.min(maxOffset, this._scrollOffset + delta));
        if (newOffset !== this._scrollOffset) {
            this._scrollOffset = newOffset;
            this._markDirty();
        }
    }

    // Mark content as changed; if client is waiting, schedule a frame
    _markDirty() {
        this._dirty = true;
        if (this._updateRequested) {
            this._updateRequested = false;
            this._dirty = false;
            this._scheduleFrame();
        }
    }

    _handleKey(keysym) {
        // Enter
        if (keysym === 0xff0d) {
            const cmd = this._inputBuffer.trim();
            this._inputBuffer = '';
            // The prompt+cmd is already displayed at _lines[_cursorY]
            // Just advance to next line
            this._cursorY++;
            if (cmd) {
                this._cmdHistory.push(cmd);
                this._executeCommand(cmd);
            } else {
                this._writePrompt();
            }
            return;
        }
        // Backspace
        if (keysym === 0xff08) {
            if (this._inputBuffer.length > 0) {
                this._inputBuffer = this._inputBuffer.slice(0, -1);
                this._redrawCurrentLine();
            }
            return;
        }
        // Regular printable char
        if (keysym >= 0x20 && keysym <= 0x7e) {
            this._inputBuffer += String.fromCharCode(keysym);
            this._redrawCurrentLine();
            return;
        }
    }

    async _executeCommand(cmd) {
        // Route through the WASM serial API
        try {
            const id = ++_fetchMsgId;
            const responseJson = await new Promise(resolve => {
                _fetchPending.set(id, resolve);
                worker.postMessage({
                    type: 'fetch', id,
                    method: 'POST',
                    path: `/api/guests/${this._guestId}/exec`,
                    body: JSON.stringify({ command: cmd })
                });
            });
            const parsed = JSON.parse(responseJson);
            let output = '';
            try {
                const bodyObj = JSON.parse(parsed.body || '{}');
                output = bodyObj.output || '';
            } catch { output = parsed.body || ''; }
            if (output) {
                const lines = output.split('\n');
                for (const line of lines) {
                    this._writeLine(line);
                }
            }
        } catch (err) {
            this._writeLine(`-ash: error: ${err.message}`);
        }
        this._writePrompt();
    }

    _writeLine(text) {
        if (this._cursorY >= this._rows) {
            // Push scrolled-off line to scrollback
            const lost = this._lines.shift();
            this._scrollback.push(lost);
            if (this._scrollback.length > this._maxScrollback) this._scrollback.shift();
            this._lines.push('');
            this._cursorY = this._rows - 1;
        }
        this._lines[this._cursorY] = text;
        this._cursorY++;
        this._cursorX = 0;
        this._scrollOffset = 0; // snap to bottom on new output
        this._markDirty();
    }

    _writePrompt() {
        if (this._cursorY >= this._rows) {
            const lost = this._lines.shift();
            this._scrollback.push(lost);
            if (this._scrollback.length > this._maxScrollback) this._scrollback.shift();
            this._lines.push('');
            this._cursorY = this._rows - 1;
        }
        this._lines[this._cursorY] = this._promptStr + this._inputBuffer;
        this._cursorX = this._promptStr.length + this._inputBuffer.length;
        this._scrollOffset = 0;
        this._markDirty();
    }

    _redrawCurrentLine() {
        this._lines[this._cursorY] = this._promptStr + this._inputBuffer;
        this._cursorX = this._promptStr.length + this._inputBuffer.length;
        this._markDirty();
    }

    _sendFullFrame() {
        // RFB FramebufferUpdate message
        // Render text to RGBA pixel buffer
        const w = this._width;
        const h = this._height;
        const pixels = new Uint8Array(w * h * 4);

        // Black background
        for (let i = 3; i < pixels.length; i += 4) pixels[i] = 255;

        // Build visible lines array based on scroll offset
        const allLines = [...this._scrollback, ...this._lines];
        const totalLines = allLines.length;
        const viewEnd = totalLines - this._scrollOffset;
        const viewStart = Math.max(0, viewEnd - this._rows);

        // Render each character
        for (let row = 0; row < this._rows; row++) {
            const lineIdx = viewStart + row;
            const line = (lineIdx < totalLines ? allLines[lineIdx] : '') || '';
            // Use dimmer color when viewing scrollback
            const gr = this._scrollOffset > 0 ? 0xaa : 0xff;
            for (let col = 0; col < line.length && col < this._cols; col++) {
                this._renderChar(pixels, w, col, row, line.charCodeAt(col), 0x00, gr, 0x00);
            }
        }

        // Scrollback indicator
        if (this._scrollOffset > 0) {
            const indicator = `[scrollback: -${this._scrollOffset} lines]`;
            for (let col = 0; col < indicator.length && col < this._cols; col++) {
                this._renderChar(pixels, w, col, 0, indicator.charCodeAt(col), 0xff, 0xff, 0x00); // yellow
            }
        }

        // Cursor block (only when at bottom / live view)
        if (this._booted && this._scrollOffset === 0) {
            const cy = this._cursorY;
            const cx = this._cursorX;
            if (cx < this._cols && cy < this._rows) {
                this._renderBlock(pixels, w, cx, cy, 0x00, 0xff, 0x00);
            }
        }

        // Build RFB message: type(1) + padding(1) + numRects(2) + rect header(12) + pixels
        const rectBytes = w * h * 4;
        const msgLen = 4 + 12 + rectBytes;
        const msg = new ArrayBuffer(msgLen);
        const dv = new DataView(msg);
        dv.setUint8(0, 0);    // FramebufferUpdate
        dv.setUint8(1, 0);    // padding
        dv.setUint16(2, 1);   // 1 rectangle
        // Rectangle: x, y, w, h, encoding(raw=0)
        dv.setUint16(4, 0);   // x
        dv.setUint16(6, 0);   // y
        dv.setUint16(8, w);   // width
        dv.setUint16(10, h);  // height
        dv.setInt32(12, 0);   // encoding = raw
        new Uint8Array(msg).set(pixels, 16);
        this._deliverMessage(msg);
    }

    _renderChar(pixels, stride, col, row, charCode, r, g, b) {
        // Simple bitmap font: 8x16 monospace, render a basic glyph
        const glyph = this._getGlyph(charCode);
        const x0 = col * this._charW;
        const y0 = row * this._charH;
        for (let dy = 0; dy < 16; dy++) {
            const bits = glyph[dy] || 0;
            for (let dx = 0; dx < 8; dx++) {
                if (bits & (0x80 >> dx)) {
                    const idx = ((y0 + dy) * stride + (x0 + dx)) * 4;
                    pixels[idx] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                    pixels[idx + 3] = 255;
                }
            }
        }
    }

    _renderBlock(pixels, stride, col, row, r, g, b) {
        const x0 = col * this._charW;
        const y0 = row * this._charH;
        for (let dy = 0; dy < this._charH; dy++) {
            for (let dx = 0; dx < this._charW; dx++) {
                const idx = ((y0 + dy) * stride + (x0 + dx)) * 4;
                pixels[idx] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
    }

    _getGlyph(code) {
        // Minimal bitmap font for ASCII 0x20-0x7E
        // Using a compact 8x16 representation
        if (code < 0x20 || code > 0x7e) return _cephaTtyFont[0x20] || new Array(16).fill(0);
        return _cephaTtyFont[code] || _cephaTtyFont[0x3f] || new Array(16).fill(0); // fallback to '?'
    }

    _delay(ms) { return new Promise(r => setTimeout(r, ms)); }

    close() {
        if (this._closed) return;
        this._closed = true;
        this.readyState = 3; // CLOSED
        this._emit('close', { code: 1000, reason: '', wasClean: true });
    }
}

// ─── Compact 8×16 Bitmap Font (ASCII 0x20–0x7E) ─────────────
// Each character is 16 rows of 8-bit bitmasks.
// Generated from standard VGA/CP437 font metrics.

const _cephaTtyFont = {
    0x20: [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0], // space
    0x21: [0,0,0x18,0x3c,0x3c,0x3c,0x18,0x18,0x18,0,0x18,0x18,0,0,0,0], // !
    0x22: [0,0x66,0x66,0x66,0x24,0,0,0,0,0,0,0,0,0,0,0], // "
    0x23: [0,0,0,0x6c,0x6c,0xfe,0x6c,0x6c,0xfe,0x6c,0x6c,0,0,0,0,0], // #
    0x24: [0,0x18,0x18,0x7c,0xc6,0xc0,0x7c,0x06,0xc6,0x7c,0x18,0x18,0,0,0,0], // $
    0x25: [0,0,0,0,0xc2,0xc6,0x0c,0x18,0x30,0x66,0xc6,0x86,0,0,0,0], // %
    0x26: [0,0,0x38,0x6c,0x6c,0x38,0x76,0xdc,0xcc,0xcc,0x76,0,0,0,0,0], // &
    0x27: [0,0x30,0x30,0x30,0x60,0,0,0,0,0,0,0,0,0,0,0], // '
    0x28: [0,0,0x0c,0x18,0x30,0x30,0x30,0x30,0x30,0x18,0x0c,0,0,0,0,0], // (
    0x29: [0,0,0x30,0x18,0x0c,0x0c,0x0c,0x0c,0x0c,0x18,0x30,0,0,0,0,0], // )
    0x2a: [0,0,0,0,0x66,0x3c,0xff,0x3c,0x66,0,0,0,0,0,0,0], // *
    0x2b: [0,0,0,0,0x18,0x18,0x7e,0x18,0x18,0,0,0,0,0,0,0], // +
    0x2c: [0,0,0,0,0,0,0,0,0,0x18,0x18,0x18,0x30,0,0,0], // ,
    0x2d: [0,0,0,0,0,0,0xfe,0,0,0,0,0,0,0,0,0], // -
    0x2e: [0,0,0,0,0,0,0,0,0,0,0x18,0x18,0,0,0,0], // .
    0x2f: [0,0,0,0x02,0x06,0x0c,0x18,0x30,0x60,0xc0,0x80,0,0,0,0,0], // /
    0x30: [0,0,0x7c,0xc6,0xce,0xde,0xf6,0xe6,0xc6,0xc6,0x7c,0,0,0,0,0], // 0
    0x31: [0,0,0x18,0x38,0x78,0x18,0x18,0x18,0x18,0x18,0x7e,0,0,0,0,0], // 1
    0x32: [0,0,0x7c,0xc6,0x06,0x0c,0x18,0x30,0x60,0xc6,0xfe,0,0,0,0,0], // 2
    0x33: [0,0,0x7c,0xc6,0x06,0x06,0x3c,0x06,0x06,0xc6,0x7c,0,0,0,0,0], // 3
    0x34: [0,0,0x0c,0x1c,0x3c,0x6c,0xcc,0xfe,0x0c,0x0c,0x1e,0,0,0,0,0], // 4
    0x35: [0,0,0xfe,0xc0,0xc0,0xfc,0x06,0x06,0x06,0xc6,0x7c,0,0,0,0,0], // 5
    0x36: [0,0,0x38,0x60,0xc0,0xc0,0xfc,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0], // 6
    0x37: [0,0,0xfe,0xc6,0x06,0x0c,0x18,0x30,0x30,0x30,0x30,0,0,0,0,0], // 7
    0x38: [0,0,0x7c,0xc6,0xc6,0xc6,0x7c,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0], // 8
    0x39: [0,0,0x7c,0xc6,0xc6,0xc6,0x7e,0x06,0x06,0x0c,0x78,0,0,0,0,0], // 9
    0x3a: [0,0,0,0,0x18,0x18,0,0,0,0x18,0x18,0,0,0,0,0], // :
    0x3b: [0,0,0,0,0x18,0x18,0,0,0,0x18,0x18,0x30,0,0,0,0], // ;
    0x3c: [0,0,0,0x06,0x0c,0x18,0x30,0x60,0x30,0x18,0x0c,0x06,0,0,0,0], // <
    0x3d: [0,0,0,0,0,0x7e,0,0,0x7e,0,0,0,0,0,0,0], // =
    0x3e: [0,0,0,0x60,0x30,0x18,0x0c,0x06,0x0c,0x18,0x30,0x60,0,0,0,0], // >
    0x3f: [0,0,0x7c,0xc6,0xc6,0x0c,0x18,0x18,0x18,0,0x18,0x18,0,0,0,0], // ?
    0x40: [0,0,0,0x7c,0xc6,0xc6,0xde,0xde,0xde,0xdc,0xc0,0x7c,0,0,0,0], // @
    0x41: [0,0,0x10,0x38,0x6c,0xc6,0xc6,0xfe,0xc6,0xc6,0xc6,0,0,0,0,0], // A
    0x42: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x66,0x66,0x66,0xfc,0,0,0,0,0], // B
    0x43: [0,0,0x3c,0x66,0xc2,0xc0,0xc0,0xc0,0xc2,0x66,0x3c,0,0,0,0,0], // C
    0x44: [0,0,0xf8,0x6c,0x66,0x66,0x66,0x66,0x66,0x6c,0xf8,0,0,0,0,0], // D
    0x45: [0,0,0xfe,0x66,0x62,0x68,0x78,0x68,0x62,0x66,0xfe,0,0,0,0,0], // E
    0x46: [0,0,0xfe,0x66,0x62,0x68,0x78,0x68,0x60,0x60,0xf0,0,0,0,0,0], // F
    0x47: [0,0,0x3c,0x66,0xc2,0xc0,0xc0,0xde,0xc6,0x66,0x3a,0,0,0,0,0], // G
    0x48: [0,0,0xc6,0xc6,0xc6,0xc6,0xfe,0xc6,0xc6,0xc6,0xc6,0,0,0,0,0], // H
    0x49: [0,0,0x3c,0x18,0x18,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0], // I
    0x4a: [0,0,0x1e,0x0c,0x0c,0x0c,0x0c,0x0c,0xcc,0xcc,0x78,0,0,0,0,0], // J
    0x4b: [0,0,0xe6,0x66,0x6c,0x6c,0x78,0x6c,0x6c,0x66,0xe6,0,0,0,0,0], // K
    0x4c: [0,0,0xf0,0x60,0x60,0x60,0x60,0x60,0x62,0x66,0xfe,0,0,0,0,0], // L
    0x4d: [0,0,0xc6,0xee,0xfe,0xfe,0xd6,0xc6,0xc6,0xc6,0xc6,0,0,0,0,0], // M
    0x4e: [0,0,0xc6,0xe6,0xf6,0xfe,0xde,0xce,0xc6,0xc6,0xc6,0,0,0,0,0], // N
    0x4f: [0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0], // O
    0x50: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x60,0x60,0x60,0xf0,0,0,0,0,0], // P
    0x51: [0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0xd6,0xde,0x7c,0x0c,0x0e,0,0,0,0], // Q
    0x52: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x6c,0x66,0x66,0xe6,0,0,0,0,0], // R
    0x53: [0,0,0x7c,0xc6,0xc6,0x60,0x38,0x0c,0xc6,0xc6,0x7c,0,0,0,0,0], // S
    0x54: [0,0,0x7e,0x7e,0x5a,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0], // T
    0x55: [0,0,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0], // U
    0x56: [0,0,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x6c,0x38,0x10,0,0,0,0,0], // V
    0x57: [0,0,0xc6,0xc6,0xc6,0xc6,0xd6,0xd6,0xfe,0x6c,0x6c,0,0,0,0,0], // W
    0x58: [0,0,0xc6,0xc6,0x6c,0x38,0x38,0x38,0x6c,0xc6,0xc6,0,0,0,0,0], // X
    0x59: [0,0,0x66,0x66,0x66,0x66,0x3c,0x18,0x18,0x18,0x3c,0,0,0,0,0], // Y
    0x5a: [0,0,0xfe,0xc6,0x86,0x0c,0x18,0x30,0x62,0xc6,0xfe,0,0,0,0,0], // Z
    0x5b: [0,0,0x3c,0x30,0x30,0x30,0x30,0x30,0x30,0x30,0x3c,0,0,0,0,0], // [
    0x5c: [0,0,0,0x80,0xc0,0x60,0x30,0x18,0x0c,0x06,0x02,0,0,0,0,0], // backslash
    0x5d: [0,0,0x3c,0x0c,0x0c,0x0c,0x0c,0x0c,0x0c,0x0c,0x3c,0,0,0,0,0], // ]
    0x5e: [0x10,0x38,0x6c,0xc6,0,0,0,0,0,0,0,0,0,0,0,0], // ^
    0x5f: [0,0,0,0,0,0,0,0,0,0,0,0,0xff,0,0,0], // _
    0x60: [0,0x30,0x30,0x18,0,0,0,0,0,0,0,0,0,0,0,0], // `
    0x61: [0,0,0,0,0,0x78,0x0c,0x7c,0xcc,0xcc,0x76,0,0,0,0,0], // a
    0x62: [0,0,0xe0,0x60,0x60,0x78,0x6c,0x66,0x66,0x66,0x7c,0,0,0,0,0], // b
    0x63: [0,0,0,0,0,0x7c,0xc6,0xc0,0xc0,0xc6,0x7c,0,0,0,0,0], // c
    0x64: [0,0,0x1c,0x0c,0x0c,0x3c,0x6c,0xcc,0xcc,0xcc,0x76,0,0,0,0,0], // d
    0x65: [0,0,0,0,0,0x7c,0xc6,0xfe,0xc0,0xc6,0x7c,0,0,0,0,0], // e
    0x66: [0,0,0x38,0x6c,0x64,0x60,0xf0,0x60,0x60,0x60,0xf0,0,0,0,0,0], // f
    0x67: [0,0,0,0,0,0x76,0xcc,0xcc,0xcc,0x7c,0x0c,0xcc,0x78,0,0,0], // g
    0x68: [0,0,0xe0,0x60,0x60,0x6c,0x76,0x66,0x66,0x66,0xe6,0,0,0,0,0], // h
    0x69: [0,0,0x18,0x18,0,0x38,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0], // i
    0x6a: [0,0,0x06,0x06,0,0x0e,0x06,0x06,0x06,0x06,0x66,0x66,0x3c,0,0,0], // j
    0x6b: [0,0,0xe0,0x60,0x60,0x66,0x6c,0x78,0x6c,0x66,0xe6,0,0,0,0,0], // k
    0x6c: [0,0,0x38,0x18,0x18,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0], // l
    0x6d: [0,0,0,0,0,0xec,0xfe,0xd6,0xd6,0xd6,0xc6,0,0,0,0,0], // m
    0x6e: [0,0,0,0,0,0xdc,0x66,0x66,0x66,0x66,0x66,0,0,0,0,0], // n
    0x6f: [0,0,0,0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0], // o
    0x70: [0,0,0,0,0,0xdc,0x66,0x66,0x66,0x7c,0x60,0x60,0xf0,0,0,0], // p
    0x71: [0,0,0,0,0,0x76,0xcc,0xcc,0xcc,0x7c,0x0c,0x0c,0x1e,0,0,0], // q
    0x72: [0,0,0,0,0,0xdc,0x76,0x66,0x60,0x60,0xf0,0,0,0,0,0], // r
    0x73: [0,0,0,0,0,0x7c,0xc6,0x70,0x1c,0xc6,0x7c,0,0,0,0,0], // s
    0x74: [0,0,0x10,0x30,0x30,0xfc,0x30,0x30,0x30,0x36,0x1c,0,0,0,0,0], // t
    0x75: [0,0,0,0,0,0xcc,0xcc,0xcc,0xcc,0xcc,0x76,0,0,0,0,0], // u
    0x76: [0,0,0,0,0,0x66,0x66,0x66,0x66,0x3c,0x18,0,0,0,0,0], // v
    0x77: [0,0,0,0,0,0xc6,0xc6,0xd6,0xd6,0xfe,0x6c,0,0,0,0,0], // w
    0x78: [0,0,0,0,0,0xc6,0x6c,0x38,0x38,0x6c,0xc6,0,0,0,0,0], // x
    0x79: [0,0,0,0,0,0xc6,0xc6,0xc6,0xc6,0x7e,0x06,0x0c,0xf8,0,0,0], // y
    0x7a: [0,0,0,0,0,0xfe,0xcc,0x18,0x30,0x66,0xfe,0,0,0,0,0], // z
    0x7b: [0,0,0x0e,0x18,0x18,0x18,0x70,0x18,0x18,0x18,0x0e,0,0,0,0,0], // {
    0x7c: [0,0,0x18,0x18,0x18,0x18,0,0x18,0x18,0x18,0x18,0,0,0,0,0], // |
    0x7d: [0,0,0x70,0x18,0x18,0x18,0x0e,0x18,0x18,0x18,0x70,0,0,0,0,0], // }
    0x7e: [0,0,0x76,0xdc,0,0,0,0,0,0,0,0,0,0,0,0], // ~
};

// ─── Service Worker Registration ─────────────────────────────
// Only register if the app provides a service-worker.js (opt-in via global flag)

if ('serviceWorker' in navigator && window.__cephaServiceWorker) {
    navigator.serviceWorker.register('service-worker.js').catch(() => {});
}
