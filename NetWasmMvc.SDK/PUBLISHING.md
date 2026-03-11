<div dir="rtl">

# 📦 دليل نشر تطبيقات Cepha MVC

> هذا الدليل يشرح كيفية نشر تطبيقات **Cepha MVC** المبنية باستخدام **NetWasmMvc.SDK** للإنتاج.

---

## 1. نظرة عامة على المعمارية

تطبيقات **Cepha** هي تطبيقات **Blazor WebAssembly** مدعومة بـ **NetWasmMvc.SDK**.
تعمل بالكامل داخل المتصفح على **Worker thread**، مما يعني أنها لا تحتاج إلى خادم (server) لتشغيلها.

**الخصائص الأساسية:**

- ✅ التطبيق يعمل بالكامل في المتصفح — لا حاجة لخادم خلفي (backend server)
- ✅ المخرجات النهائية هي موقع ثابت (static site) يتكون من ملفات `HTML` / `JS` / `WASM`
- ✅ يمكن استضافته على أي خدمة استضافة ملفات ثابتة (Cloudflare Pages, GitHub Pages, Netlify, إلخ)
- ✅ مجلد المخرجات الافتراضي: `bin/Release/net10.0/publish/wwwroot/`

---

## 2. النشر باستخدام dotnet CLI

أبسط طريقة للنشر هي استخدام أمر `dotnet publish`:

```bash
dotnet publish -c Release
```

**المخرجات:** سيتم إنشاء الملفات الجاهزة للنشر في المسار التالي:

```
bin/Release/net10.0/publish/wwwroot/
```

> 💡 **ملاحظة:** ابتداءً من الإصدار **v1.0.53+** من SDK، يتم إنشاء ملف `_headers` تلقائيًا عند النشر. هذا الملف يحتوي على الـ headers المطلوبة لتشغيل WebAssembly بشكل صحيح على منصات مثل Cloudflare Pages و Netlify.

---

## 3. النشر باستخدام Visual Studio

### النشر إلى مجلد (Folder Publish)

1. انقر بزر الماوس الأيمن على المشروع في **Solution Explorer**
2. اختر **Publish**
3. اختر **Folder** كهدف للنشر
4. حدد المسار: `bin/Release/net10.0/publish/wwwroot/`
5. اضغط **Publish**

### التشغيل أثناء التطوير

للتطوير المحلي، يمكنك استخدام **WasmAppHost** المدمج:

- اضغط **F5** أو استخدم أمر `dotnet run`
- سيتم تشغيل التطبيق محليًا باستخدام WasmAppHost المضمّن

---

## 4. الـ Headers المطلوبة للاستضافة

تطبيقات **Blazor WebAssembly** تتطلب headers معينة لتعمل بشكل صحيح، خاصةً لدعم `SharedArrayBuffer` و تحميل ملفات `.wasm`.

### ملف `_headers` (لـ Cloudflare Pages و Netlify)

هذا الملف يُنشأ تلقائيًا ابتداءً من SDK v1.0.53+. صيغته:

```
/*
  X-Content-Type-Options: nosniff
  Cross-Origin-Embedder-Policy: require-corp
  Cross-Origin-Opener-Policy: same-origin
  Access-Control-Allow-Origin: *

/_framework/*.wasm
  Content-Type: application/wasm
  Cache-Control: public, max-age=31536000, immutable
```

**شرح الـ Headers:**

| Header | الوظيفة |
|--------|---------|
| `Cross-Origin-Embedder-Policy: require-corp` | مطلوب لتفعيل `SharedArrayBuffer` |
| `Cross-Origin-Opener-Policy: same-origin` | مطلوب لتفعيل `SharedArrayBuffer` |
| `X-Content-Type-Options: nosniff` | حماية أمنية تمنع المتصفح من تخمين نوع المحتوى |
| `Access-Control-Allow-Origin: *` | يسمح بتحميل الموارد من أي مصدر |

> ⚠️ بدون هذه الـ Headers، لن يعمل التطبيق وستظهر أخطاء CORS في وحدة التحكم (Console).

---

### إعدادات Apache (ملف `.htaccess`)

```apache
<IfModule mod_headers.c>
  Header set Cross-Origin-Embedder-Policy "require-corp"
  Header set Cross-Origin-Opener-Policy "same-origin"
</IfModule>
AddType application/wasm .wasm
```

---

### إعدادات nginx

```nginx
add_header Cross-Origin-Embedder-Policy "require-corp" always;
add_header Cross-Origin-Opener-Policy "same-origin" always;
types { application/wasm wasm; }
```

---

### إعدادات IIS (ملف `web.config`)

```xml
<system.webServer>
  <httpProtocol>
    <customHeaders>
      <add name="Cross-Origin-Embedder-Policy" value="require-corp" />
      <add name="Cross-Origin-Opener-Policy" value="same-origin" />
    </customHeaders>
  </httpProtocol>
  <staticContent>
    <mimeMap fileExtension=".wasm" mimeType="application/wasm" />
    <mimeMap fileExtension=".dat" mimeType="application/octet-stream" />
    <mimeMap fileExtension=".blat" mimeType="application/octet-stream" />
  </staticContent>
  <rewrite>
    <rules>
      <rule name="SPA Fallback" stopProcessing="true">
        <match url=".*" />
        <conditions>
          <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
          <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
        </conditions>
        <action type="Rewrite" url="/" />
      </rule>
    </rules>
  </rewrite>
</system.webServer>
```

**ملاحظات حول إعداد IIS:**

- تأكد من تثبيت **URL Rewrite Module** لتفعيل قاعدة SPA Fallback
- أنواع MIME لملفات `.dat` و `.blat` مطلوبة لتحميل بيانات .NET Runtime
- قاعدة **SPA Fallback** تُعيد توجيه جميع الطلبات إلى `index.html` لدعم التوجيه (routing) في التطبيق

---

## 5. أهداف النشر (Deployment Targets)

### ☁️ Cloudflare Pages (الخيار الموصى به)

أسهل وأسرع طريقة للنشر. يمكنك استخدام أداة `cepha` التفاعلية:

```bash
cepha publish        # قائمة تفاعلية لاختيار منصة النشر
```

أو مباشرةً باستخدام **Wrangler**:

```bash
npx wrangler pages deploy "bin/Release/net10.0/publish/wwwroot" --project-name my-app
```

**المميزات:**
- دعم تلقائي لملف `_headers`
- شبكة CDN عالمية سريعة
- شهادة SSL مجانية
- نشر تلقائي من Git

---

### 🔷 Azure Static Web Apps

```bash
npx swa deploy "bin/Release/net10.0/publish/wwwroot" --env production
```

يمكنك أيضًا ربط المستودع مباشرة من بوابة Azure لنشر تلقائي مع كل push.

---

### 🐙 GitHub Pages

**الخطوات:**

1. انشر محتويات مجلد `publish/wwwroot/` إلى فرع `gh-pages`:

```bash
# مثال باستخدام gh-pages
git subtree push --prefix bin/Release/net10.0/publish/wwwroot origin gh-pages
```

2. أضف ملف `.nojekyll` فارغ في جذر الفرع (لمنع GitHub من معالجة الملفات بـ Jekyll):

```bash
touch .nojekyll
```

3. أنشئ ملف `404.html` يُعيد التوجيه إلى `index.html` لدعم SPA routing:

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <script>
    // SPA redirect for GitHub Pages
    sessionStorage.redirect = location.href;
    location.replace('/');
  </script>
</head>
<body></body>
</html>
```

> ⚠️ GitHub Pages لا يدعم تعيين headers مخصصة. قد تحتاج إلى حل بديل لـ `Cross-Origin-Embedder-Policy` إذا كان التطبيق يعتمد على `SharedArrayBuffer`.

---

### 🐳 Docker (مع nginx)

```dockerfile
FROM nginx:alpine
COPY bin/Release/net10.0/publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
```

ملف `nginx.conf` المرافق:

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    add_header Cross-Origin-Embedder-Policy "require-corp" always;
    add_header Cross-Origin-Opener-Policy "same-origin" always;

    types {
        application/wasm wasm;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /_framework/ {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
}
```

**بناء وتشغيل الصورة:**

```bash
docker build -t my-cepha-app .
docker run -d -p 8080:80 my-cepha-app
```

---

## 6. CephaKit (خادم Node.js)

**CephaKit** هو خادم Node.js يُستخدم لتشغيل التطبيق مع دعم server-side rendering (SSR) ووضع API أثناء التطوير.

### التشغيل

```bash
cepha kit                  # تشغيل على المنفذ الافتراضي
cepha kit --port 3001      # تشغيل على منفذ محدد
```

### كيف يعمل

- يستخدم ملف `cepha-server.mjs` لتشغيل .NET WASM داخل بيئة Node.js
- يوفر واجهة API ودعم SSR للتطبيق

### ⚠️ تنبيه مهم

> **CephaKit مخصص للتطوير فقط.** للإنتاج، استخدم الاستضافة الثابتة (static hosting) كما هو موضح في القسم السابق.

---

## 7. استكشاف الأخطاء وإصلاحها (Troubleshooting)

### ❌ أخطاء CORS في وحدة التحكم

**السبب:** الـ header `Cross-Origin-Embedder-Policy` غير مُعيَّن على الخادم.

**الحل:** تأكد من إضافة الـ headers التالية في إعدادات الخادم:
```
Cross-Origin-Embedder-Policy: require-corp
Cross-Origin-Opener-Policy: same-origin
```

---

### ❌ ملفات WASM لا تتحمل

**السبب:** نوع MIME لملفات `.wasm` غير مُسجَّل على الخادم.

**الحل:** أضف نوع MIME `application/wasm` لملفات `.wasm` في إعدادات الخادم.

---

### ❌ صفحة بيضاء فارغة

**السبب:** إعداد SPA Fallback غير مُفعَّل. عند الانتقال إلى مسار (route) غير `/`، الخادم يُرجع خطأ 404 بدلاً من تحميل `index.html`.

**الحل:** قم بإعداد قاعدة إعادة كتابة (rewrite rule) لتوجيه جميع الطلبات إلى `index.html`. راجع إعدادات الخادم المناسب في القسم 4.

---

### ❌ حجم التحميل كبير (بطء التحميل الأولي)

**السبب:** عدم تفعيل الضغط (compression) على الخادم.

**الحل:** فعّل ضغط **gzip** أو **Brotli** على الخادم. معظم خدمات CDN (مثل Cloudflare) تفعل ذلك تلقائيًا.

```nginx
# مثال لـ nginx
gzip on;
gzip_types application/wasm application/javascript application/json;
```

---

### ❌ رسائل تصحيح كثيرة من EF Core في وحدة التحكم

**السبب:** مستوى التسجيل (log level) لـ `Microsoft.EntityFrameworkCore.*` مُعيَّن على Debug.

**الحل:** تم إصلاح هذا في إعدادات NLog. تأكد من تعيين مستوى التسجيل إلى `Warn` لـ `Microsoft.EntityFrameworkCore.*`:

```xml
<logger name="Microsoft.EntityFrameworkCore.*" minlevel="Warn" writeTo="console" final="true" />
```

---

## 8. مرجع إعدادات ملف المشروع (`.csproj`)

ملف `.csproj` النموذجي لتطبيق Cepha MVC:

```xml
<Project Sdk="NetWasmMvc.SDK/1.0.52">
  <PropertyGroup>
    <RootNamespace>MyApp</RootNamespace>
    <AssemblyName>MyApp</AssemblyName>
    <!-- إلغاء الإنشاء التلقائي لملف _headers عند النشر -->
    <!-- <CephaGenerateHeaders>false</CephaGenerateHeaders> -->
    <!-- تفعيل التشغيل التلقائي لـ CephaKit عند البناء (وضع التطوير) -->
    <!-- <CephaKitEnabled>true</CephaKitEnabled> -->
    <!-- <CephaKitPort>3001</CephaKitPort> -->
  </PropertyGroup>
</Project>
```

### شرح الخصائص

| الخاصية | الوصف | القيمة الافتراضية |
|---------|-------|-------------------|
| `CephaGenerateHeaders` | التحكم في إنشاء ملف `_headers` تلقائيًا عند النشر | `true` |
| `CephaKitEnabled` | تفعيل التشغيل التلقائي لخادم CephaKit عند البناء | `false` |
| `CephaKitPort` | تحديد منفذ خادم CephaKit | `3000` |

---

## 📋 ملخص سريع

| الخطوة | الأمر / الإجراء |
|--------|-----------------|
| بناء للإنتاج | `dotnet publish -c Release` |
| مجلد المخرجات | `bin/Release/net10.0/publish/wwwroot/` |
| نشر على Cloudflare | `cepha publish` أو `npx wrangler pages deploy ...` |
| نشر على Azure | `npx swa deploy ...` |
| تشغيل محلي (تطوير) | `dotnet run` أو `cepha kit` |

---

> 📖 لمزيد من المعلومات، راجع [README.md](README.md) الخاص بالمشروع.

</div>
