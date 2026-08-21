# 🌐 Web UI

WatermarkRemover ships with a **plug-and-play Astro web UI** that wraps the
HTTP API (`serve` on `:5080`) into a single-page dashboard. The UI is a
**static site** — pure HTML, CSS, and a few kilobytes of vanilla JS — so it
deploys anywhere a folder of files can be served.

> **TL;DR.** `npm run build` in `/web`, then `watermarkremover serve` exposes
> both the API and the UI on `http://localhost:5080/`. No reverse proxy, no
> Node server, no framework runtime shipped to the browser.
>
> **Even shorter.** Download a release binary, run `watermarkremover serve`,
> open `http://localhost:5080/`. The web UI is embedded in the binary.

---

## Out of the box

Three install paths, all of which produce a working API + UI on the same
port. Pick whichever fits:

| Path                              | One-liner                                                              | UI included? |
|-----------------------------------|------------------------------------------------------------------------|--------------|
| Download a release binary         | `watermarkremover serve --port 5080`                                   | ✅ embedded in the binary (single-file, `IncludeAllContentForSelfExtract`) |
| `git clone` + build from source   | `make serve` *(Linux/mac)* / `scripts\build.ps1 -Serve` *(Windows)*    | ✅ built and synced into `wwwroot/` before `dotnet publish` |
| `docker run`                      | `docker run --rm -p 5080:5080 techbuzzz/watermarkremover:latest`       | ✅ built in the `webbuild` stage of the multi-stage Dockerfile |

For the **pre-built binary** path, the `.csproj` marks `wwwroot/**/*` as
`<Content>` with `CopyToOutputDirectory=PreserveNewest`, and the release
workflow uses `-p:IncludeAllContentForSelfExtract=true` so the static
bundle is embedded in the single-file output. When the user runs the
binary, the content is extracted to a temp directory (e.g.
`%LOCALAPPDATA%\Temp\.net\watermarkremover\<hash>\wwwroot\`) and the
ASP.NET Core static-file middleware finds it transparently — no extra
unzip step, no `node`, no `npm`.

For the **from-source** path, `make build` (or `scripts\build.ps1`) runs
`npm install && npm run build` in `web/` first, which produces `dist/`
and then runs `scripts/sync.mjs` to copy it into
`src/WatermarkRemover.CLI/wwwroot/`. The `dotnet build` / `dotnet
publish` step that follows treats `wwwroot/` as ordinary content and
ships it with the binary. A `make serve` (or `-Serve` on the PowerShell
script) does both back-to-back and then launches the server.

For the **Docker** path, the multi-stage `Dockerfile` adds a `webbuild`
stage (`FROM node:22-alpine`) that runs the same npm build and overlays
the result into the .NET source tree before `dotnet publish`. The final
runtime image ships the binary plus `wwwroot/` together.

---

## What you get

A single page at `/` with four tabs in one card-shaped "box":

| Tab       | What it does                                  | API endpoints used               |
|-----------|-----------------------------------------------|----------------------------------|
| **Text**     | Paste text, run Layers A/B/C, see diff & detections | `POST /clean/text`, `POST /detect/text` |
| **Markdown** | Paste markdown, strip AI artifacts, preview cleaned | `POST /clean/markdown`             |
| **File**     | Drag-and-drop a JPEG/PNG/PDF/DOCX/HTML, strip or inspect metadata | `POST /clean/file`, `POST /inspect/file` |
| **Image**    | Drag-and-drop an image, run LaMa inpainting, preview & download | `POST /clean/image`, `POST /detect/image` |

Tabs follow the WAI-ARIA tabs pattern: `←` / `→` keys move between them,
`Home` / `End` jump to the first / last. The last-active tab is restored on
reload via `localStorage`.

The page respects `prefers-color-scheme` (light / dark) and is fully
responsive (down to 360 px wide).

---

## Quick start (dev)

```bash
# Terminal 1 — start the .NET API
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080

# Terminal 2 — start the Astro dev server (hot reload, separate port)
cd web
npm install
npm run dev
# → http://localhost:4321
```

The dev server proxies nothing — it expects the API at `PUBLIC_API_URL` (which
defaults to `http://localhost:5080`). To point at a different instance, copy
`web/.env.example` to `web/.env` and edit.

```ini
# web/.env
PUBLIC_API_URL=http://192.168.1.50:5080
PUBLIC_API_KEY=s3cret
```

---

## Building for production

```bash
cd web
npm run build
```

This runs `astro build` then `node scripts/sync.mjs`, which copies the
`dist/` output into `src/WatermarkRemover.CLI/wwwroot/`. The .NET CLI's
`.csproj` marks that directory as `<Content>` with
`CopyToOutputDirectory=PreserveNewest`, so it ships in the published binary.

After `npm run build`, run the server and the UI is automatically served on
the same port:

```bash
dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080
# → open http://localhost:5080/
```

The CLI logs a confirmation when the bundle is mounted. Pass `--no-ui` to
disable the web server (for headless API-only deployments).

---

## Configuration

| Env var            | Default                      | Required | Notes |
|--------------------|------------------------------|----------|-------|
| `PUBLIC_API_URL`   | `http://localhost:5080`      | no       | No trailing slash. Must include scheme. |
| `PUBLIC_API_KEY`   | *(empty)*                    | no       | If set, the browser sends it as `X-API-Key`. |

Both variables are `PUBLIC_*` — the values are embedded in the static bundle.
**Do not** set `PUBLIC_API_KEY` to a sensitive secret in a public-internet
deploy; anyone can read the built JS with browser dev-tools. The UI
console-logs a warning when an API key is set but the page is being served
from a non-localhost host.

### CLI flags (server side)

| Flag                  | Default        | Notes |
|-----------------------|----------------|-------|
| `--cors-origins`      | `*` (no key) / `http://localhost:4321,http://localhost:5080` (key set) | Comma-separated origins. Overrides env `WATERMARKREMOVER_CORS_ORIGINS`. |
| `--no-ui`             | `false`        | Skip serving `wwwroot/` even when present. |

Resolution order: `--cors-origins` > `WATERMARKREMOVER_CORS_ORIGINS` env var
> built-in default. Use `--cors-origins=""` to disable CORS entirely.

---

## Standalone deploys (no .NET on the host)

The Astro bundle is just a folder of static files. Drop `web/dist/` on any
static host and configure the env vars at build time:

```bash
# Vercel / Netlify / Cloudflare Pages / GH Pages / S3 / nginx …
cd web
PUBLIC_API_URL=https://api.your-host.example.com \
  npm run build
# upload web/dist/ to your host
```

For hosts that serve over a different origin than the API, also set
`WATERMARKREMOVER_CORS_ORIGINS` on the server to include the page's origin.

### nginx example (same host, two ports)

```nginx
server {
  listen 80;
  server_name wr.local;

  # Astro UI
  root /var/www/wr-web/dist;
  index index.html;
  location / { try_files $uri $uri/ /index.html; }

  # .NET API
  location /api/ { proxy_pass http://127.0.0.1:5080/; }
}
```

Build the UI with `PUBLIC_API_URL=/api` and you're done.

---

## Architecture

```
   browser  ──HTTP──▶  Astro static bundle (dist/)
                       one index.html, ~3 KB JS, no framework runtime
                                │
                                │ fetch() with optional X-API-Key
                                ▼
                       WatermarkRemover API
                       (Kestrel, :5080)
                       UseStaticFiles("wwwroot/")  ◀── dist/ copied here at build
                       MapEndpoints(...)
                                │
                                ▼
                       Text / Metadata / Image pipelines (unchanged)
```

Why this is "plug and play":

1. **One binary, one port.** `watermarkremover serve` exposes both the API and
   the UI on the same port. No reverse proxy.
2. **One env var to point elsewhere.** `PUBLIC_API_URL=http://other:5080`
   re-points the whole UI. That's it.
3. **Zero JS framework.** No React/Vue/Svelte runtime. Adding a tab is one
   new `.astro` file plus one tiny `<script>`.
4. **Static output, no Node server.** `output: 'static'` — the bundle is a
   folder of HTML/CSS/JS. Drop it anywhere.

---

## Tech stack

- **Astro 5.x**, `output: 'static'`, islands architecture.
- **TypeScript** in `strict` mode.
- **`astro:env` schema** for type-safe configuration (`PUBLIC_API_URL`,
  `PUBLIC_API_KEY`).
- **Vanilla JS** in `<script>` blocks (one per tab). No UI framework.
- **Vitest** for unit tests (`npm test`).
- **`<8 KB` JS shipped per tab**, `<50 KB` total page weight gzipped.
- **No CSS framework.** Hand-written CSS with custom-property design tokens
  and `prefers-color-scheme` support.

---

## Repository layout

```
web/
├── package.json              # deps + scripts (dev, build, preview, test, sync)
├── astro.config.mjs          # output: 'static', env schema
├── tsconfig.json             # extends astro/tsconfigs/strict
├── .env.example              # PUBLIC_API_URL, PUBLIC_API_KEY
├── scripts/
│   └── sync.mjs              # copies dist/ → src/WatermarkRemover.CLI/wwwroot
├── public/
│   └── favicon.svg
├── src/
│   ├── env.d.ts              # ImportMetaEnv extension
│   ├── pages/
│   │   └── index.astro       # the one page; renders <Box />
│   ├── components/
│   │   ├── Box.astro         # card shell + tab nav
│   │   └── tabs/
│   │       ├── TextTab.astro
│   │       ├── MarkdownTab.astro
│   │       ├── FileTab.astro
│   │       └── ImageTab.astro
│   ├── widgets/              # pure TS, no framework
│   │   ├── text-widget.ts
│   │   ├── markdown-widget.ts
│   │   ├── file-widget.ts
│   │   └── image-widget.ts
│   ├── lib/
│   │   ├── api.ts            # fetch wrappers for all 8 endpoints
│   │   ├── config.ts         # reads PUBLIC_API_URL / PUBLIC_API_KEY
│   │   └── format.ts         # formatBytes, formatMs, diff helpers
│   └── styles/
│       └── global.css        # design tokens + components
└── test/
    └── astro-env-client.stub.ts   # vitest stub for astro:env/client
```

---

## Testing

```bash
cd web
npm test           # vitest — 20+ unit tests across lib/
npm run typecheck  # astro check — 0 errors
npm run build      # astro build + sync.mjs
```

Manual smoke test against a running `serve`:

1. `dotnet run --project src/WatermarkRemover.CLI -- serve --port 5080`
2. `cd web && npm run preview` (serves dist/ on :4321)
3. Open `http://localhost:4321/`
4. Click each tab, submit a request, confirm a result renders.

---

## Security notes

- `PUBLIC_*` env vars are **embedded in the JS bundle**. They are not
  server-side secrets. Treat them as public.
- `--api-key` auth is enforced on every endpoint **except** `/health`. The
  UI sends `X-API-Key` when `PUBLIC_API_KEY` is set; if the API runs without
  `--api-key`, the browser just doesn't send the header.
- The UI does not implement its own auth / rate limit — the server does.
- CORS is **opt-in**. When you turn on `--api-key`, the default CORS list
  narrows from `*` to local dev hosts. Adjust with `--cors-origins` for
  production.

---

## Troubleshooting

| Symptom                                              | Cause / fix |
|------------------------------------------------------|-------------|
| `WatermarkRemover` box shows "API unreachable"       | `PUBLIC_API_URL` is wrong, or the API is down, or CORS blocked the request. |
| `401 API key required` red banner                    | Server is running with `--api-key` but `PUBLIC_API_KEY` is empty (or vice-versa). |
| `429 Rate limited` amber banner                      | Server is throttling (100 req/min/IP). Wait, or lower the request rate. |
| Static assets return 404 in dev (`/_astro/...`)      | Restart the dev server. Vite/Astro occasionally needs a cold start. |
| Bundle not mounted in the published binary           | The .csproj `<Content>` rule is intact but the `wwwroot/` directory is empty — run `npm run build`. |
| `npm run build` fails with `formatMs is not exported`| You have stale dependencies. Run `npm install`. |
