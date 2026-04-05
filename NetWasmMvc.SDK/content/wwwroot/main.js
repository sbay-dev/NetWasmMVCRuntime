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
// In WASM mode there is no real QEMU process. VNC WebSocket
// connections are intercepted and display an honest status screen
// explaining that VM display requires server mode.
// NO fake terminal, NO simulated OpenWrt — just clear information.

const _OriginalWebSocket = window.WebSocket;

window.WebSocket = function(url, protocols) {
    if (typeof url === 'string' && url.includes('/nc-vnc/')) {
        const guestIdMatch = url.match(/\/nc-vnc\/([^/?#]+)/);
        const guestId = guestIdMatch ? guestIdMatch[1] : 'unknown';
        console.log(`🧬 VNC: browser-wasm mode — no QEMU process (guest ${guestId.substring(0, 12)})`);
        return new CephaStatusSocket(url, guestId);
    }
    if (protocols !== undefined) return new _OriginalWebSocket(url, protocols);
    return new _OriginalWebSocket(url);
};
window.WebSocket.CONNECTING = 0;
window.WebSocket.OPEN = 1;
window.WebSocket.CLOSING = 2;
window.WebSocket.CLOSED = 3;
window.WebSocket.prototype = _OriginalWebSocket.prototype;

// ─── CephaStatusSocket: RFB status display for WASM mode ────
// Implements minimal RFB 3.8 handshake to satisfy noVNC, then
// renders a static status screen. No fake terminal, no simulation.

class CephaStatusSocket {
    constructor(url, guestId) {
        this.url = url;
        this._guestId = guestId;
        this.readyState = 0;
        this.binaryType = 'arraybuffer';
        this.protocol = 'binary';
        this.extensions = '';
        this.bufferedAmount = 0;
        this.onopen = null;
        this.onmessage = null;
        this.onclose = null;
        this.onerror = null;
        this._listeners = {};
        this._phase = 'version';
        this._closed = false;
        this._width = 640;
        this._height = 384;
        this._charW = 8;
        this._charH = 16;
        this._cols = 80;
        this._rows = 24;
        this._frameSent = false;

        Promise.resolve().then(() => {
            if (!this._closed) this._startHandshake();
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
    _emit(type, props) {
        const evt = Object.assign({ type }, props || {});
        try {
            if (this['on' + type]) this['on' + type](evt);
            (this._listeners[type] || []).forEach(fn => fn(evt));
        } catch (e) { /* ignore */ }
    }

    _startHandshake() {
        this.readyState = 1;
        this._emit('open');
        Promise.resolve().then(() => {
            if (!this._closed) {
                const ver = new TextEncoder().encode('RFB 003.008\n');
                this._deliver(ver.buffer);
            }
        });
    }

    send(data) {
        if (this._closed) return;
        switch (this._phase) {
            case 'version':
                this._phase = 'security';
                this._deliver(new Uint8Array([1, 1]).buffer);
                break;
            case 'security':
                this._phase = 'secresult';
                const sr = new ArrayBuffer(4);
                new DataView(sr).setUint32(0, 0);
                this._deliver(sr);
                break;
            case 'secresult':
                this._phase = 'init';
                this._sendServerInit();
                break;
            case 'init':
            case 'running':
                this._phase = 'running';
                this._handleMsg(data instanceof Uint8Array ? data : new Uint8Array(data));
                break;
        }
    }

    _deliver(buffer) {
        if (this._closed) return;
        setTimeout(() => {
            if (!this._closed) this._emit('message', { data: buffer });
        }, 0);
    }

    _sendServerInit() {
        const name = 'NetWasmMvc.SDK';
        const nameBytes = new TextEncoder().encode(name);
        const buf = new ArrayBuffer(24 + nameBytes.length);
        const dv = new DataView(buf);
        dv.setUint16(0, this._width);
        dv.setUint16(2, this._height);
        dv.setUint8(4, 32); dv.setUint8(5, 24); dv.setUint8(6, 0); dv.setUint8(7, 1);
        dv.setUint16(8, 255); dv.setUint16(10, 255); dv.setUint16(12, 255);
        dv.setUint8(14, 16); dv.setUint8(15, 8); dv.setUint8(16, 0);
        dv.setUint32(20, nameBytes.length);
        new Uint8Array(buf).set(nameBytes, 24);
        this._deliver(buf);
    }

    _handleMsg(bytes) {
        if (bytes.length === 0) return;
        if (bytes[0] === 3 && !this._frameSent) {
            this._frameSent = true;
            requestAnimationFrame(() => this._sendStatusFrame());
        }
    }

    _sendStatusFrame() {
        const w = this._width, h = this._height;
        const px = new Uint8Array(w * h * 4);
        // Dark background (#1a1a2e)
        for (let i = 0; i < w * h; i++) {
            px[i*4] = 0x1a; px[i*4+1] = 0x1a; px[i*4+2] = 0x2e; px[i*4+3] = 255;
        }
        const lines = [
            '',
            '',
            '   NetWasmMvc.SDK  -  Display Surface',
            '',
            '   Mode: Browser WASM (no QEMU process)',
            '   Guest: ' + this._guestId.substring(0, 16),
            '',
            '   VM display requires server mode.',
            '   Build with Microsoft.NET.Sdk.Web',
            '   to enable QEMU, VNC, SSH, and LuCI.',
            '',
            '   WASM capabilities:',
            '     MVC routing and views ........... OK',
            '     Database (SQLite + OPFS) ........ OK',
            '     Identity and sessions ........... OK',
            '     SignalR hubs .................... OK',
            '',
            '   Server-only (requires QEMU):',
            '     VM display (VNC) ............... --',
            '     Shell / serial console ......... --',
            '     LuCI web interface ............. --',
            '     SSH access ..................... --',
        ];
        for (let row = 0; row < lines.length && row < this._rows; row++) {
            const line = lines[row];
            const isTitle = row === 2;
            const isSection = line.startsWith('   WASM') || line.startsWith('   Server');
            const isOK = line.includes('... OK');
            const isDash = line.includes('... --');
            let r = 0x88, g = 0x88, b = 0x88;
            if (isTitle) { r = 0x64; g = 0xb5; b = 0xf6; }
            else if (isSection) { r = 0xff; g = 0xd5; b = 0x4f; }
            else if (isOK) { r = 0x4e; g = 0xc9; b = 0xb0; }
            else if (isDash) { r = 0x88; g = 0x88; b = 0x88; }
            else { r = 0xcc; g = 0xcc; b = 0xcc; }
            for (let col = 0; col < line.length && col < this._cols; col++) {
                this._putChar(px, w, col, row, line.charCodeAt(col), r, g, b);
            }
        }
        // Border line at top
        for (let x = 0; x < w; x++) {
            const i = x * 4;
            px[i] = 0x64; px[i+1] = 0xb5; px[i+2] = 0xf6; px[i+3] = 255;
        }
        const msgLen = 4 + 12 + w * h * 4;
        const msg = new ArrayBuffer(msgLen);
        const dv = new DataView(msg);
        dv.setUint8(0, 0); dv.setUint8(1, 0); dv.setUint16(2, 1);
        dv.setUint16(4, 0); dv.setUint16(6, 0);
        dv.setUint16(8, w); dv.setUint16(10, h);
        dv.setInt32(12, 0);
        new Uint8Array(msg).set(px, 16);
        this._deliver(msg);
    }

    _putChar(px, stride, col, row, code, r, g, b) {
        if (code < 0x20 || code > 0x7e) return;
        const glyph = _cephaStatusFont[code];
        if (!glyph) return;
        const x0 = col * this._charW, y0 = row * this._charH;
        for (let dy = 0; dy < 16; dy++) {
            const bits = glyph[dy] || 0;
            for (let dx = 0; dx < 8; dx++) {
                if (bits & (0x80 >> dx)) {
                    const i = ((y0 + dy) * stride + (x0 + dx)) * 4;
                    px[i] = r; px[i+1] = g; px[i+2] = b; px[i+3] = 255;
                }
            }
        }
    }

    close() {
        if (this._closed) return;
        this._closed = true;
        this.readyState = 3;
        this._emit('close', { code: 1000, reason: '', wasClean: true });
    }
}

// Minimal bitmap font (ASCII 0x20-0x7E, 8x16) — only characters used in status display
const _cephaStatusFont = {
    0x20: [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
    0x21: [0,0,0x18,0x3c,0x3c,0x3c,0x18,0x18,0x18,0,0x18,0x18,0,0,0,0],
    0x28: [0,0,0x0c,0x18,0x30,0x30,0x30,0x30,0x30,0x18,0x0c,0,0,0,0,0],
    0x29: [0,0,0x30,0x18,0x0c,0x0c,0x0c,0x0c,0x0c,0x18,0x30,0,0,0,0,0],
    0x2b: [0,0,0,0,0x18,0x18,0x7e,0x18,0x18,0,0,0,0,0,0,0],
    0x2c: [0,0,0,0,0,0,0,0,0,0x18,0x18,0x18,0x30,0,0,0],
    0x2d: [0,0,0,0,0,0,0xfe,0,0,0,0,0,0,0,0,0],
    0x2e: [0,0,0,0,0,0,0,0,0,0,0x18,0x18,0,0,0,0],
    0x2f: [0,0,0,0x02,0x06,0x0c,0x18,0x30,0x60,0xc0,0x80,0,0,0,0,0],
    0x30: [0,0,0x7c,0xc6,0xce,0xde,0xf6,0xe6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x31: [0,0,0x18,0x38,0x78,0x18,0x18,0x18,0x18,0x18,0x7e,0,0,0,0,0],
    0x32: [0,0,0x7c,0xc6,0x06,0x0c,0x18,0x30,0x60,0xc6,0xfe,0,0,0,0,0],
    0x33: [0,0,0x7c,0xc6,0x06,0x06,0x3c,0x06,0x06,0xc6,0x7c,0,0,0,0,0],
    0x34: [0,0,0x0c,0x1c,0x3c,0x6c,0xcc,0xfe,0x0c,0x0c,0x1e,0,0,0,0,0],
    0x35: [0,0,0xfe,0xc0,0xc0,0xfc,0x06,0x06,0x06,0xc6,0x7c,0,0,0,0,0],
    0x36: [0,0,0x38,0x60,0xc0,0xc0,0xfc,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x37: [0,0,0xfe,0xc6,0x06,0x0c,0x18,0x30,0x30,0x30,0x30,0,0,0,0,0],
    0x38: [0,0,0x7c,0xc6,0xc6,0xc6,0x7c,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x39: [0,0,0x7c,0xc6,0xc6,0xc6,0x7e,0x06,0x06,0x0c,0x78,0,0,0,0,0],
    0x3a: [0,0,0,0,0x18,0x18,0,0,0,0x18,0x18,0,0,0,0,0],
    0x41: [0,0,0x10,0x38,0x6c,0xc6,0xc6,0xfe,0xc6,0xc6,0xc6,0,0,0,0,0],
    0x42: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x66,0x66,0x66,0xfc,0,0,0,0,0],
    0x43: [0,0,0x3c,0x66,0xc2,0xc0,0xc0,0xc0,0xc2,0x66,0x3c,0,0,0,0,0],
    0x44: [0,0,0xf8,0x6c,0x66,0x66,0x66,0x66,0x66,0x6c,0xf8,0,0,0,0,0],
    0x45: [0,0,0xfe,0x66,0x62,0x68,0x78,0x68,0x62,0x66,0xfe,0,0,0,0,0],
    0x46: [0,0,0xfe,0x66,0x62,0x68,0x78,0x68,0x60,0x60,0xf0,0,0,0,0,0],
    0x47: [0,0,0x3c,0x66,0xc2,0xc0,0xc0,0xde,0xc6,0x66,0x3a,0,0,0,0,0],
    0x48: [0,0,0xc6,0xc6,0xc6,0xc6,0xfe,0xc6,0xc6,0xc6,0xc6,0,0,0,0,0],
    0x49: [0,0,0x3c,0x18,0x18,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0],
    0x4b: [0,0,0xe6,0x66,0x6c,0x6c,0x78,0x6c,0x6c,0x66,0xe6,0,0,0,0,0],
    0x4c: [0,0,0xf0,0x60,0x60,0x60,0x60,0x60,0x62,0x66,0xfe,0,0,0,0,0],
    0x4d: [0,0,0xc6,0xee,0xfe,0xd6,0xc6,0xc6,0xc6,0xc6,0xc6,0,0,0,0,0],
    0x4e: [0,0,0xc6,0xe6,0xf6,0xfe,0xde,0xce,0xc6,0xc6,0xc6,0,0,0,0,0],
    0x4f: [0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x50: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x60,0x60,0x60,0xf0,0,0,0,0,0],
    0x51: [0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0xc6,0xd6,0xde,0x7c,0x0c,0x0e,0,0,0],
    0x52: [0,0,0xfc,0x66,0x66,0x66,0x7c,0x6c,0x66,0x66,0xe6,0,0,0,0,0],
    0x53: [0,0,0x7c,0xc6,0xc6,0x60,0x38,0x0c,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x54: [0,0,0x7e,0x7e,0x5a,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0],
    0x55: [0,0,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x56: [0,0,0xc6,0xc6,0xc6,0xc6,0xc6,0xc6,0x6c,0x38,0x10,0,0,0,0,0],
    0x57: [0,0,0xc6,0xc6,0xc6,0xc6,0xd6,0xd6,0xfe,0xee,0xc6,0,0,0,0,0],
    0x58: [0,0,0xc6,0xc6,0x6c,0x38,0x38,0x6c,0xc6,0xc6,0xc6,0,0,0,0,0],
    0x61: [0,0,0,0,0,0x78,0x0c,0x7c,0xcc,0xcc,0x76,0,0,0,0,0],
    0x62: [0,0,0xe0,0x60,0x60,0x78,0x6c,0x66,0x66,0x66,0x7c,0,0,0,0,0],
    0x63: [0,0,0,0,0,0x7c,0xc6,0xc0,0xc0,0xc6,0x7c,0,0,0,0,0],
    0x64: [0,0,0x1c,0x0c,0x0c,0x3c,0x6c,0xcc,0xcc,0xcc,0x76,0,0,0,0,0],
    0x65: [0,0,0,0,0,0x7c,0xc6,0xfe,0xc0,0xc6,0x7c,0,0,0,0,0],
    0x66: [0,0,0x1c,0x36,0x32,0x30,0x78,0x30,0x30,0x30,0x78,0,0,0,0,0],
    0x67: [0,0,0,0,0,0x76,0xcc,0xcc,0xcc,0x7c,0x0c,0xcc,0x78,0,0,0],
    0x68: [0,0,0xe0,0x60,0x60,0x6c,0x76,0x66,0x66,0x66,0xe6,0,0,0,0,0],
    0x69: [0,0,0x18,0x18,0,0x38,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0],
    0x6b: [0,0,0xe0,0x60,0x60,0x66,0x6c,0x78,0x6c,0x66,0xe6,0,0,0,0,0],
    0x6c: [0,0,0x38,0x18,0x18,0x18,0x18,0x18,0x18,0x18,0x3c,0,0,0,0,0],
    0x6d: [0,0,0,0,0,0xec,0xfe,0xd6,0xd6,0xd6,0xc6,0,0,0,0,0],
    0x6e: [0,0,0,0,0,0xdc,0x66,0x66,0x66,0x66,0x66,0,0,0,0,0],
    0x6f: [0,0,0,0,0,0x7c,0xc6,0xc6,0xc6,0xc6,0x7c,0,0,0,0,0],
    0x70: [0,0,0,0,0,0xdc,0x66,0x66,0x66,0x7c,0x60,0x60,0xf0,0,0,0],
    0x71: [0,0,0,0,0,0x76,0xcc,0xcc,0xcc,0x7c,0x0c,0x0c,0x1e,0,0,0],
    0x72: [0,0,0,0,0,0xdc,0x76,0x66,0x60,0x60,0xf0,0,0,0,0,0],
    0x73: [0,0,0,0,0,0x7c,0xc6,0x70,0x1c,0xc6,0x7c,0,0,0,0,0],
    0x74: [0,0,0x10,0x30,0x30,0xfc,0x30,0x30,0x30,0x36,0x1c,0,0,0,0,0],
    0x75: [0,0,0,0,0,0xcc,0xcc,0xcc,0xcc,0xcc,0x76,0,0,0,0,0],
    0x76: [0,0,0,0,0,0x66,0x66,0x66,0x66,0x3c,0x18,0,0,0,0,0],
    0x77: [0,0,0,0,0,0xc6,0xc6,0xd6,0xd6,0xfe,0x6c,0,0,0,0,0],
    0x78: [0,0,0,0,0,0xc6,0x6c,0x38,0x38,0x6c,0xc6,0,0,0,0,0],
    0x79: [0,0,0,0,0,0xc6,0xc6,0xc6,0xc6,0x7e,0x06,0x0c,0xf8,0,0,0],
};

// ─── Service Worker Registration ─────────────────────────────
// Only register if the app provides a service-worker.js (opt-in via global flag)

if ('serviceWorker' in navigator && window.__cephaServiceWorker) {
    navigator.serviceWorker.register('service-worker.js').catch(() => {});
}