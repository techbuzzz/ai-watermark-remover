# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **How to read this file:**
> - **Added** — new features.
> - **Changed** — changes in existing functionality.
> - **Deprecated** — soon-to-be-removed features.
> - **Removed** — now-removed features.
> - **Fixed** — bug fixes.
> - **Security** — vulnerability fixes (also see [SECURITY.md](./SECURITY.md)).

---

## [Unreleased]

### Added
- **MCP packaging + Docker for MCP (`Dockerfile` + `docker-compose.yml`
  + `.github/workflows/release.yml` + `docs/MCP.md`,
  WR-S20 / WR-P631 + WR-P633)** — the MCP story is now first-class in
  both the release pipeline and the container image. `Dockerfile`
  now `EXPOSE`s **both** `5080` (HTTP API + Astro UI) and `5090` (MCP
  Streamable HTTP); the header documents a `serve-mcp --transport
  http` CMD variant alongside the existing `serve` default; and a
  regression-guard comment in the runtime stage explains how to
  override `HEALTHCHECK` when running MCP-only. `docker-compose.yml`
  grows two new top-level entries: an `mcp` long-running service
  that runs `serve-mcp --transport http --host 0.0.0.0 --port 5090`
  with the same hardening posture as the HTTP API service
  (read-only root fs, tmpfs `/tmp`, `no-new-privileges`, dedicated
  `healthcheck` probing `http://127.0.0.1:5090/health`); and a
  `clean` service under a `clean` profile for one-shot CLI
  invocations (`docker compose run --rm clean clean-text
  --input /data/in.txt --output /data/out.txt`). The release
  workflow (`.github/workflows/release.yml`) gets a new
  `Smoke-test 'serve-mcp' sub-command` step that runs **after**
  the binary is published and **before** the zip is uploaded: it
  locates the apphost (handling both `watermarkremover` and
  `watermarkremover.exe` per RID), invokes `serve-mcp --help`, and
  asserts the help text mentions both `--transport` and
  `stdio` / `http` — so a regression that accidentally trims the
  `ModelContextProtocol` SDK out of the single-file bundle fails
  the build instead of shipping a broken MCP integration. The MCP
  NuGet packages (`ModelContextProtocol` 2.2.0 and
  `ModelContextProtocol.AspNetCore` 2.2.0) were already pinned in
  `src/Directory.Packages.props`; the new
  `McpDockerPackagingTests` class guards them so a future
  central-management refactor that drops one is caught.
  `docs/MCP.md → Docker` grows three subsections: the original
  `docker run` recipe, a new `Docker Compose` walkthrough
  (`docker compose up mcp`, side-by-side with the API, the `clean`
  profile), and a new `Building the image locally` block.
  **17 new xUnit tests** in
  `WatermarkRemover.CLI.Tests/McpDockerPackagingTests` (file
  presence for `Dockerfile` / `docker-compose.yml` / `release.yml`
  / `Directory.Packages.props` / `docs/MCP.md`; structural
  assertions: `EXPOSE 5080 5090` is parsed by a digit-only regex
  that handles the multi-line and single-line shapes; the `mcp:`
  compose service block is matched; the `serve-mcp` + `"http"` +
  `5090` triplet is asserted in the compose body; the MCP
  healthcheck probes the right port; the `watermarkremover`
  service stays intact; the release workflow invokes
  `serve-mcp --help` and asserts the transport-flag help text;
  the docs section mentions `docker compose` and port `5090`).
  Build clean (0 warnings, 0 errors), **438 xUnit tests total**
  (81 + 35 + 9 + 26 + 39 + 248) + **13 Node tests**, all green.

- **VS Code extension (`vscode/watermark-remover/` + `docs/VS-CODE.md`,
  WR-S19 / WR-P625)** — the project now ships a first-party VS Code
  extension pre-wired into the repo. The extension is a **thin UI
  client** over the `watermarkremover` CLI — it does not re-implement
  any cleaning logic; every command spawns the binary as a child
  process and pipes data through it. New `vscode/watermark-remover/`
  tree with: `package.json` (publisher `techbuzzz`, `engines.vscode
  ^1.85.0`, three commands `watermarkremover.cleanText` /
  `watermarkremover.cleanFile` / `watermarkremover.detectText` under
  the `WatermarkRemover` category, four `contributes.menus` slots —
  `editor/context`, `editor/context/contextual`, `explorer/context`,
  `explorer/context/contextual` — plus the `commandPalette` slot with
  `when` clauses, four `contributes.configuration` settings:
  `watermarkremover.binaryPath` (default `watermarkremover`),
  `watermarkremover.preferMcp` (reserved, default `false`),
  `watermarkremover.statistical` (default `false`),
  `watermarkremover.showNotifications` (default `true`),
  `activationEvents` for the three `onCommand:` events, `scripts.build
  = tsc -p .` and `vscode:prepublish = npm run build`, full
  `devDependencies` for `typescript` / `@types/vscode` / `@types/node`,
  and a marketplace `icon` pointing at the bundled 128 KB PNG);
  `tsconfig.json` (ES2022 target, strict, `rootDir: src/`,
  `outDir: out/`, `include: src/**/*`); `src/extension.ts` (≈ 350
  LOC, dependency-free at runtime — only uses `node:child_process` and
  the `vscode` module, exports `activate()` that registers the three
  commands and `deactivate()` for symmetry; custom
  `WatermarkRemoverNotFoundError` and `WatermarkRemoverFailedError`
  exception types so error messages stay user-friendly; the
  `runBinary()` wrapper handles spawn-ENOENT, exit-code mapping, and
  a 60-second timeout; `cleanTextCommand()` replaces the editor
  selection with the cleaned text and reports the character delta in a
  status-bar notification; `cleanFileCommand()` walks one or more
  URIs from `explorer/context`, treats exit-code 3 as "skipped
  (unsupported format)" and shows a per-file success / skip / fail
  summary; `detectTextCommand()` spawns `detect-text --stdin --json`
  and opens the formatted result in a new editor tab beside the
  current one); `skills/` folder with the master `watermark-remover`
  SKILL.md (`compatibility: vscode, opencode, claude-code, minimax-code,
  cursor, continue` so any agent that consumes SKILL.md learns the
  extension) plus the five per-format skills re-shipped from the
  repo-root `skills/` (`clean-text`, `clean-markdown`, `clean-file`,
  `clean-image`, `detect` — each carries the same `SKILL.md` / `run.sh`
  / `run.ps1` triple the repo-root `skills/` already had); bundled
  `icon.png` (the same 128 KB PNG used by the MiniMax Code plugin,
  re-used to keep the package small); a marketplace `README.md`
  (features, requirements, three install paths — Marketplace / `.vsix`
  / source — three usage flows — text selection / file in Explorer /
  command palette — settings table, source-mode `dotnet run` recipe,
  the dependency-free architecture diagram, known limitations);
  `CHANGELOG.md` (Keep-a-Changelog format with an `[Unreleased]
  → Added` section); `.vscodeignore` (excludes `src/`, `tsconfig.json`,
  `node_modules/`, dev logs); `.gitignore`; and a Node `node --test`
  suite under `test/extension.test.js` with **16 green tests**
  covering top-level field presence, name + publisher, SemVer,
  `engines.vscode ^1.85.0`, `engines.node >=18`, activationEvents for
  all three commands, `contributes.commands` shape, every menu slot
  for editor and explorer (with `commandPalette`), the four
  `configuration` settings, the build + `vscode:prepublish` + test
  scripts, the three devDependencies, `src/extension.ts` referencing
  every command and importing `child_process`, `tsconfig.json` strict
  with `src/**/*` include, the six skill folders, the master
  frontmatter mentioning `vscode`, and the README having install
  instructions. New `docs/VS-CODE.md` is the end-to-end reference:
  why-VS-Code, what-the-extension-ships, three install paths
  (Marketplace / `.vsix` / source), three usage flows
  (`cleanText` selection / `cleanFile` Explorer / `detectText`
  palette), settings table, source-mode `dotnet run` wrapper recipe,
  bundled-skills rationale, MCP-vs-extension complement, a 7-row
  troubleshooting table (binary not on `$PATH`, spawn ENOENT,
  unsupported format, Remote / WSL workspaces, image-inpaint not in
  the extension, skill not picked up by Continue / Cline), and a
  reference list. `docs/MCP.md → VS Code` is a new sibling section
  between Continue and the npm package: four install recipes
  (Marketplace, stdio `.vscode/mcp.json`, `dotnet run` source mode,
  Streamable HTTP for Docker / remote) plus a one-line "restart VS
  Code" tip. README grows a **"VS Code users get a first-party
  extension"** callout under `## 🧠 Agent skills` (parallel to the
  existing OpenCode / Claude Code / MiniMax Code callouts) with the
  three context-menu verbs spelled out, and the `## 📚 Documentation`
  footer gains a `🆚 docs/VS-CODE.md` row. The `## 🤖 MCP server`
  preamble also lists VS Code alongside the other supported hosts.
  **30 new xUnit tests** in `WatermarkRemover.CLI.Tests`:
  `VsCodeExtensionTests` — directory + 7 file-presence theory rows
  (`package.json`, `tsconfig.json`, `README.md`, `CHANGELOG.md`,
  `.vscodeignore`, `.gitignore`, `src/extension.ts`), `skills/`
  presence, `test/*.test.js` non-empty, 6 skill folders each with
  `SKILL.md` (theory rows), `package.json` valid JSON, all 11
  required top-level fields, name + publisher, SemVer, `engines.vscode
  ^1.85.0` (regex `^\^1\.(8[5-9]|9[0-9])\.0$`), `engines.node >=18`,
  activationEvents for all three commands, all three commands in
  `contributes.commands` with non-empty title + `WatermarkRemover`
  category, the four `editor/context` / `editor/context/contextual` /
  `commandPalette` menus include `cleanText` + `detectText`, the two
  `explorer/context` / `explorer/context/contextual` menus include
  `cleanFile`, all four `configuration` settings, `scripts.build =
  tsc -p .` + `vscode:prepublish = npm run build` + test script
  starts with `node --test `, all three devDependencies
  (`typescript` / `@types/vscode` / `@types/node`),
  `repository.directory = "vscode/watermark-remover"`,
  `tsconfig.json` valid JSON + ES2022 + strict + `src/**/*` include,
  `src/extension.ts` registers all three commands and imports
  `child_process` and invokes the three CLI subcommands
  (`clean-text` / `clean-file` / `detect-text`), the README is
  substantial and has `Requirements` + `Extension settings` + `VS
  Code` mentions, the CHANGELOG has `Unreleased` + `Added`, the
  `.vscodeignore` excludes `src/` + `tsconfig.json`, the master skill
  frontmatter `compatibility:` line lists `vscode`, `docs/MCP.md`
  has a `### VS Code` header and references `vscode/watermark-remover`,
  `docs/VS-CODE.md` is present with `Install` / `Usage` / `Extension
  settings` / `Troubleshooting` sections, and the parent README
  contains the "VS Code users get a first-party extension" callout
  and links to `docs/VS-CODE.md`. Solution build clean (0 warnings,
  0 errors), **437 tests total** (421 xUnit + 16 Node), all green.
- **Cursor / Continue MCP config + `@watermarkremover/mcp` npm package
  (`npm/watermarkremover-mcp/` + `docs/MCP.md`, WR-S18 / WR-P624,
  WR-P632)** — the project now ships a **zero-dependency npm wrapper**
  at `npm/watermarkremover-mcp/` and expanded Cursor / Continue
  install recipes in `docs/MCP.md`. The package contains
  `package.json` (`name: @watermarkremover/mcp`,
  `bin: watermarkremover-mcp`, `engines.node >= 18`, `os`/`cpu`
  allowlists, `repository.directory` pinned to the npm subfolder,
  zero runtime dependencies); `index.js` — the `bin` entry, a Node
  shebang + `child_process.spawn` of `serve-mcp` with
  `stdio: 'inherit'` and signal forwarding (SIGINT/SIGTERM/SIGHUP);
  `postinstall.js` — the install hook that delegates to
  `lib/install.js` with the package's own version as the expected
  release tag; `lib/binary.js` — the RID table (4 entries:
  linux-x64, linux-arm64, darwin-x64, win-x64 — locked to the
  release workflow matrix), `detectRuntimeId()`,
  `releaseAssetUrl()`, `installedBinaryPath()`, and `resolveBinary()`
  with the priority order
  `WATERMARKREMOVER_BINARY > bin/ > $PATH > unresolved`;
  `lib/install.js` — the in-tree HTTP downloader (uses Node's
  built-in `http`/`https` so the package has zero deps, follows
  GitHub's 302 → `objects.githubusercontent.com` redirects, capped
  at 5 hops) and a hand-rolled ZIP central-directory reader that
  handles STORED and DEFLATE entries (no `adm-zip` / `yauzl`);
  `test/binary.test.js` — 13 Node `node:test` cases covering RID
  table, platform detection, asset URL shape, installed path per
  platform, resolution priority chain, the flat-vs-nested entry
  matcher, and a synthetic ZIP round-trip against the in-tree
  extractor; `README.md` (install, postinstall contract, supported
  RIDs, env-var escape hatches, `WATERMARKREMOVER_BINARY` source-mode
  swap) and `.gitignore` (`bin/`). The postinstall is **idempotent**
  (re-runs are no-ops when the binary is present), **soft-fails**
  (network errors print a remediation message to stderr and exit 0
  so `npm install` still succeeds), and **opt-out-able** via
  `WR_SKIP_BINARY_DOWNLOAD=1`. `WR_FORCE_BINARY_DOWNLOAD=1` forces a
  re-download. `docs/MCP.md` is rewritten: the **Cursor** and
  **Continue** sections each expand from one snippet to **three**
  (release binary on `$PATH`, the new npm wrapper, and `dotnet run`
  source mode); a new `### npm package (@watermarkremover/mcp)`
  section sits between Continue and Docker with a host-by-host
  wiring table, two full example snippets, the source-mode swap
  recipe, and the supported-RIDs matrix; the troubleshooting table
  gains two new rows (npm wrapper exit 127, npm postinstall yellow
  warning). README's `## 🤖 MCP server` section grows a "Cursor /
  Continue users get an npm wrapper" callout with the snippet shape;
  the docs-link footer adds a `📦 npm/watermarkremover-mcp/` row.
  28 new xUnit tests in `WatermarkRemover.CLI.Tests`
  (`NpmPackageTests` — 20 — directory + 6 file-presence theory
  rows, valid `package.json` JSON, scoped name, SemVer, `bin` →
  `index.js`, `postinstall` script, test script, `files` allowlist,
  `engines.node >= 18`, `repository.directory`, `index.js`
  shebang + `serve-mcp` arg + `stdio: 'inherit'`, `postinstall.js`
  delegation, `binary.js` advertises every supported RID,
  `install.js` honours skip/force env vars; and
  `CursorContinueConfigTests` — 8 — doc presence, `### Cursor` and
  `### Continue` and `### npm package` headers, the embedded Cursor
  + Continue config JSON blocks parse + register `watermarkremover`
  with the right shape, the dedicated npm-section Cursor + Continue
  `npx` snippets parse + reference `@watermarkremover/mcp`).
  Solution build clean (0 warnings, 0 errors), **375 tests total**
  (81 + 35 + 26 + 9 + 39 + 185), all green;
  `node --test` on the npm package's own suite reports 13/13 green.
- **MiniMax Code integration (`minimax-code/watermark-remover/` + `docs/MINIMAX-CODE.md`, WR-S17 / WR-P623)** —
  the project now ships a first-class MiniMax Code integration
  pre-wired into the repo as a V1 local plugin package. The plugin
  lives at `minimax-code/watermark-remover/` with the canonical
  layout: `.minimax-plugin/plugin.json` (schemaVersion 1, name
  `watermark-remover`, category `Code`, 6 skills, 1 MCP server, 4
  example queries, no apps, no `auth`/`oauth`/credentials);
  `servers.mcp.json` declaring the stdio MCP server
  `watermarkremover` that runs `watermarkremover serve-mcp` (30 s
  timeout, `env: {}`); a 128 KB `icon.png` (validated PNG
  signature 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A, picked from
  the local MiniMax Code Code-category pool); six skills under
  `skills/` — master `watermark-remover/SKILL.md` (routing table
  + CLI fallback + worked examples, MiniMax-Code-specific — no
  slash-command auto-discovery section, points at the MCP tools
  directly) plus the five per-format skills re-shipped from
  `skills/` (their `name` field matches their directory per the V1
  spec, so the directory uses the longer `watermark-clean-text`
  shape, not the shorter `clean-text`); three forward-looking
  slash-command files under `commands/` (`wr-clean-text.md`,
  `wr-clean-file.md`, `wr-detect.md`) — the V1 manifest does not
  yet declare `commands` as a first-class capability, but the files
  are present and will surface in any future MiniMax Code version
  that auto-discovers `commands/*.md` the same way OpenCode does;
  and a plugin-level `README.md` that documents the install +
  prerequisites + what-the-agent-sees layout. `docs/MCP.md →
  MiniMax Code` is rewritten to point at the new install method
  (drop the folder into the MiniMax Code plugin directory, toggle
  on, done) instead of the old hand-edited `mcp-config.json`
  recipe. New `docs/MINIMAX-CODE.md` is the end-to-end reference
  — package layout, install matrix (Linux / macOS / Windows +
  source-mode swap for `dotnet run`), verify-the-install
  walkthrough, slash-command status section explaining the
  forward-looking `commands/` folder, MCP transport notes
  (stdio default, Streamable HTTP swap with `servers.mcp.json`
  edit, port 5090, 30 s timeout, stderr logging contract), full
  CLI fallback list, a 10-row troubleshooting table, and an
  "Assumptions and open questions" section that calls out where
  the V1 spec is silent (icon auto-discovery, `commands/`
  forwarding). README's `## 🧠 Agent skills` section gains a
  parallel "MiniMax Code users get a parallel pre-wired
  integration" callout with the OS-specific copy commands and
  link to `docs/MINIMAX-CODE.md`; the docs-link footer adds a
  `🧩 docs/MINIMAX-CODE.md` row. 25 new tests in
  `MinimaxCodePluginManifestTests` cover: plugin folder presence,
  manifest JSON validity, all 10 required V1 fields, schemaVersion
  == 1, name in kebab-case regex + directory-name match, version
  in SemVer, category in the 10-value whitelist, description
  non-empty, every example query non-empty, `apps` is the empty
  array, at least one effective capability (mcpServers ∪ skills),
  no unknown / forbidden fields (`auth`, `oauth`, `client_id`,
  `client_secret`, `token`, `apiKey`, `api_key`), every
  `mcpServers` path resolves, every `skills` path resolves, the
  `icon` file exists, the icon's first 8 bytes are the PNG
  signature, the package contains no path escapes, the package
  fits within V1 limits (≤ 1024 regular files, ≤ 2048 entries,
  ≤ 64 MiB total, ≤ 16 MiB per file), no reparse points /
  symlinks, README present, MCP config is valid JSON, MCP config
  has `schemaVersion` + non-empty `mcpServers` object, every MCP
  server uses a supported transport (`stdio` / `streamable-http`
  / `sse`), stdio commands are slash-free / PATH-resolved,
  remote URLs are `http(s)://`, every skill's frontmatter `name`
  matches its directory, every skill has a non-empty
  `description`, and the three forward-looking slash-command
  files are present. Build clean (0 warnings, 0 errors); 346
  tests total, all green; the new tests are the
  +25 (131 → 156) jump in the `WatermarkRemover.CLI.Tests`
  count. JSON validated with `node -e JSON.parse`; icon signature
  validated with `[System.IO.File]::ReadAllBytes` byte
  comparison.
- **Claude Code integration (`.claude/` + `docs/CLAUDE-CODE.md`, WR-S16 / WR-P622)** —
  the project now ships a first-class Claude Code integration pre-wired
  into the repo. New `.claude/skills/watermark-remover/SKILL.md` is the
  master skill, mirroring the OpenCode one but with Claude-Code-specific
  routing (no auto-discovered slash-command files — Claude invokes the
  MCP tools as ordinary tool calls). Drop-in
  `.claude/skills/watermark-remover/mcp-config.json` is a project-level
  `mcpServers` snippet the user can copy into `.mcp.json` (or merge
  into `.claude/settings.json`) for a checked-in registration that
  survives `claude mcp add` typos; the recommended path is still the
  one-liner `claude mcp add watermarkremover -- watermarkremover
  serve-mcp`, which the README and `docs/CLAUDE-CODE.md` both surface.
  Optional `.claude/skills/watermark-remover/hooks.json` registers a
  `UserPromptSubmit` command hook that pipes the user's pasted text
  through `clean_text` and injects the cleaned version back as
  `hookSpecificOutput.additionalContext`; the script
  `hooks/auto-clean.js` is intentionally a no-op when the CLI is
  missing, returns non-zero, or the cleaned text equals the input,
  so the hook is invisible in the common case and only adds context
  when the pasted text actually contained invisible / zero-width /
  homoglyph characters. New `docs/CLAUDE-CODE.md` is the
  end-to-end reference: install shapes (one-liner, project-local
  `mcp-config.json`, `~/.claude/settings.json` merge, `dotnet run`
  source-mode swap), verification (`claude mcp list` / `claude mcp
  get watermarkremover`), the auto-clean hook contract (project-local
  vs. global install via `skills/install.sh --agent claude`,
  tuning tips, failure modes), a "what the agent sees" walkthrough,
  and a 7-row troubleshooting table. README's `## 🤖 MCP server`
  reference callout now points at `docs/CLAUDE-CODE.md` next to the
  existing `docs/MCP.md` link; the `## 🧠 Agent skills` section
  gains a parallel "Claude Code users get the integration
  pre-wired" callout with the one-liner and a link to
  `docs/CLAUDE-CODE.md`; the docs-link footer adds a `🪝
  docs/CLAUDE-CODE.md` row. Build clean (0 warnings), 321 tests,
  all green. JSON files validated with `node -e JSON.parse`;
  `auto-clean.js` exits 0 on empty / malformed stdin and on
  missing CLI (graceful no-op contract).
- **OpenCode integration (`.opencode/` + slash commands, WR-S15 / WR-P621)** —
  the project now ships a first-class OpenCode integration pre-wired
  into the repo so the agent learns WatermarkRemover as soon as
  `.opencode/opencode.jsonc` is opened. New
  `.opencode/skills/watermark-remover/SKILL.md` is the master skill
  — it teaches the agent the routing table (text → `clean_text`,
  metadata → `clean_file`, inpaint → `clean_image`, forensic check
  → `detect_text` / `detect_markdown`, etc.), the CLI fallback
  shape, the error-handling contract, and points at the five
  per-format skills under `skills/`. Three new slash commands under
  `.opencode/commands/` — `/wr-clean-text <text>` (strip invisible
  characters + AI watermarks), `/wr-clean-file <path>` (strip
  metadata from a file, preserve pixels), and `/wr-detect <text>`
  (forensic read-only check that returns a `WatermarkMatch[]` with
  `vendor` / `kind` / `evidence`) — appear in the TUI slash-command
  picker without any extra wiring because OpenCode auto-discovers
  `.opencode/commands/<name>.md`. The project-level
  `.opencode/opencode.jsonc` gains a `watermarkremover` MCP entry
  under the `mcp` key, default `enabled: false` (matches the
  existing `github` pattern — the binary may not be on `$PATH` for
  every contributor); an inline comment explains how to enable it
  and how to swap the command for `dotnet run --project
  src/WatermarkRemover.CLI -- serve-mcp` for source-mode
  development. The `permission.skill` allowlist adds
  `watermark-remover` plus the five per-format skills so the
  OpenCode permission system does not gate them. `docs/MCP.md →
  OpenCode` is rewritten to match the actual OpenCode spec
  (MCP-via-`opencode.jsonc`, skills under `.opencode/skills/`,
  commands under `.opencode/commands/`, slash-command markdown
  format with `$ARGUMENTS`) — replaces the old
  `.opencode/mcp-config.json` recipe, which was not a real
  OpenCode file. The section now also documents: pre-shipped
  artifacts in this repo, install onto a *different* project via
  `cp -R` or `skills/install.sh --agent opencode --target
  .opencode/skills`, source-mode `dotnet run` swap, and the
  rationale for `enabled: false` default. README's `## 🤖 MCP
  server (serve-mcp)` block gets a parallel OpenCode one-liner
  next to the existing `claude mcp add` line, and the `## 🧠
  Agent skills` block grows an "OpenCode users get the
  integration pre-wired" callout pointing at
  `.opencode/skills/watermark-remover/SKILL.md` and
  `.opencode/commands/`.
- **Agent skills (`skills/` directory + installer, WR-S14 / WR-P611..WR-P617)** —
  new top-level `skills/` directory that ships five drop-in skill
  packages — `watermark-clean-text`, `watermark-clean-markdown`,
  `watermark-clean-file`, `watermark-clean-image`, and
  `watermark-detect` — each as a self-contained folder with a
  `SKILL.md` (YAML-frontmatter trigger description + usage guide for
  the agent) plus POSIX (`run.sh`) and Windows (`run.ps1`) wrappers
  that pipe input through the `watermarkremover` CLI. The skills map
  1-to-1 onto the existing CLI commands and MCP tools:
  `clean-text` ↔ `clean_text`, `clean-markdown` ↔ `clean_markdown`,
  `clean-file` ↔ `clean_file` / `inspect_file`,
  `clean-image` ↔ `clean_image` / `detect_watermark`,
  `detect-*` ↔ `detect_text` / `detect_markdown` /
  `detect_watermark`. Each `SKILL.md` covers: trigger conditions,
  the canonical MCP tool call (with full request/response JSON), the
  CLI fallback, 2-3 worked examples (EN + RU for text, before/after
  for markdown, file-type mapping table for file, mask guidance for
  image, vendor / kind / evidence interpretation for detect), error
  handling, language notes, and a cross-reference to the matching
  `docs/MCP.md` / `docs/ARCHITECTURE.md` section. New
  `skills/install.sh` and `skills/install.ps1` install any subset
  into the target agent's skills directory with one command:
  `./skills/install.sh --agent opencode|claude|minimax|cursor|continue|generic|auto`,
  plus `--target <path>`, `--dry-run`, `--list`, `--help`; auto-detect
  probes CWD for `.opencode/` / `.claude/` / `.minimax/` and falls
  back to `~/.config/watermarkremover/skills/`; environment overrides
  pin individual agents (`WATERMARKREMOVER_SKILLS_AGENT`,
  `WATERMARKREMOVER_SKILLS_CLAUDE_DIR`,
  `WATERMARKREMOVER_SKILLS_OPENCODE_DIR`,
  `WATERMARKREMOVER_SKILLS_MINIMAX_DIR`,
  `WATERMARKREMOVER_SKILLS_GENERIC_DIR`). The shell scripts and the
  new C# `SkillsInstallerTargetResolver` (in
  `WatermarkRemover.CLI/Infrastructure/`) share the same resolution
  rules — 30 new xUnit tests in
  `WatermarkRemover.CLI.Tests/SkillsInstallerTargetResolverTests`
  cover every agent name + alias (case-insensitive), the home
  resolution fallback (`HOME` → `USERPROFILE` → CWD for generic),
  every env-override path (including blank/whitespace being
  ignored), the auto-detect probe (pinned env wins, marker order:
  opencode → claude → minimax, fall-through to generic when no
  markers), the argument-validation contract (null env / empty cwd /
  unknown agent), and the `KnownAgents` / `SkillSubdir` invariants.
  New `docs/SKILLS.md` is the full reference: layout, install
  options, resolution matrix (single source of truth =
  `SkillsInstallerTargetResolver`), per-skill deep-dive, MCP vs
  skill guidance (recommended: use both), and a troubleshooting
  table. README gains a new `## 🧠 Agent skills` section with a
  one-liner per skill, an install matrix, and a "Full reference:
  docs/SKILLS.md" pointer; the table of contents gets a matching
  entry; the `📚 Documentation` block gets a new
  `docs/SKILLS.md` row. Build clean (0 warnings, 0 errors), 319
  tests total (30 new in
  `SkillsInstallerTargetResolverTests`), all green.
- **MCP server docs (`docs/MCP.md`, WR-S13 / WR-P605)** — new
  end-user-and-developer reference for the `serve-mcp` command.
  Sections: architecture diagram (agent → transport → `WatermarkRemover.Mcp`
  → existing pipeline interfaces); transports (stdio default, Streamable
  HTTP stateless, legacy SSE noted as "not currently shipped"); tool
  reference for all 8 tools (`clean_text`, `clean_markdown`,
  `clean_file`, `clean_image`, `detect_text`, `detect_markdown`,
  `inspect_file`, `detect_watermark`) with full parameter tables,
  request/response JSON examples, and the output-block conventions
  (`TextContentBlock`, `EmbeddedResourceBlock` with `BlobResourceContents`,
  `ImageContentBlock`); configuration (`mcp:` block in `config.yaml`,
  CLI flag reference, resolution order CLI > config > default);
  install recipes for Claude Code (`claude mcp add`), OpenCode
  (`.opencode/mcp-config.json`), MiniMax Code, Cursor
  (`~/.cursor/mcp.json`), Continue (`~/.continue/config.json`), and
  Docker (`docker run -p 5090:5090 … serve-mcp --transport http`),
  plus a JSON-RPC `tools/list` / `tools/call` smoke-test recipe; a
  troubleshooting table for the most common install / runtime issues
  (model missing, port in use, API key 401, rate-limit 429, etc.);
  and the canonical SDK reference links
  (https://github.com/modelcontextprotocol/csharp-sdk and
  https://csharp.sdk.modelcontextprotocol.io/) with the relevant
  entry points (`AddMcpServer`, `WithStdioServerTransport`,
  `WithHttpTransport`, `WithToolsFromAssembly`,
  `[McpServerToolType]` / `[McpServerTool]` attributes, the MCP
  transports spec, the tools spec). README "🤖 MCP server" section
  gets a "Full reference: docs/MCP.md" pointer; the
  "📚 Documentation" section gains a dedicated `docs/MCP.md` row.
  Build clean (0 warnings, 0 errors), 289 tests still green.
- **MCP server integration tests (WR-S12 / WR-P604)** — new
  `JsonRpcIntegrationTests` fixture in `WatermarkRemover.Mcp.Tests`
  that hosts a real `McpServer` in-process (the same composition root
  the `serve-mcp` stdio command uses — `AddWatermarkRemoverCore /
  Text / Metadata / Image / Mcp` plus a local `IInpaintRunner` so no
  ONNX model is needed) bound to a paired `System.IO.Pipelines.Pipe`
  pair via `WithStreamServerTransport(input, output)`, then connects
  an `McpClient` over the matching `StreamClientTransport`. No
  subprocess, no socket, no port — the SDK's official in-process
  testing pattern. 11 new tests cover the full JSON-RPC surface:
  `Initialize_Handshake_ReturnsServerInfo`,
  `Initialize_Handsshake_AdvertisesToolsCapability`,
  `ToolsList_Returns8Tools`, `CleanText_RemovesZwsp`,
  `CleanMarkdown_StripsFrontmatter`,
  `DetectText_FindsVendorWatermark` (Cyrillic homoglyph + ZWSP run
  triggers both Claude signatures),
  `InspectFile_ReturnsMetadataEntries` (tEXt chunk round-trip),
  `CleanFile_ReturnsBlobResource` (cleaned bytes as
  `EmbeddedResourceBlock` with `image/png` MIME),
  `CleanImage_ReturnsImageContentBlock` (PNG re-encode as
  `ImageContentBlock` with the `0x89 P N G` signature verified),
  `DetectWatermark_ReturnsRegions` (real `MaskGenerator` over a
  32×32 fixture with a semi-transparent overlay), and
  `EmptyInput_ReturnsToolError` (McpException surfaces as
  `CallToolResult.IsError = true`, not a protocol error, per the MCP
  spec). The class-level `McpJsonRpcHost` (`IAsyncLifetime` +
  `IClassFixture<>`) starts the host, starts the client, and cleans
  up both at the end. `Microsoft.Extensions.Hosting` added to
  `Directory.Packages.props`; `Microsoft.Extensions.Logging.Abstractions`
  bumped to `10.0.10` and `Microsoft.Extensions.DependencyInjection.Abstractions`
  to `10.0.10` to match the transitive graph. Solution build clean,
  0 warnings, 289 tests total (39 in `WatermarkRemover.Mcp.Tests`),
  all green.
- **MCP server core (`WatermarkRemover.Mcp` project)** — new
  transport-agnostic class library that exposes the full
  WatermarkRemover pipeline as eight Model Context Protocol tools
  (built on the official `ModelContextProtocol` C# SDK 2.2.0):
  `clean_text`, `clean_markdown`, `clean_file`, `clean_image`,
  `detect_text`, `detect_markdown`, `inspect_file`, and
  `detect_watermark`. Each tool is a `[McpServerToolType]`-attributed
  static class with a single `[McpServerTool]` method that calls the
  existing pipeline interface (`ITextCleaningPipeline`,
  `IMarkdownCleaner`, `IFileCleanerRouter`, `IImageCleaningPipeline`) —
  no new business logic, no duplication. Tools resolve their
  dependencies via DI parameter binding (the SDK auto-resolves
  `ITextCleaningPipeline` / `IMarkdownCleaner` / `IFileCleanerRouter`
  / `IImageCleaningPipeline` / `AppConfig` / `ILoggerFactory` from
  the same service collection the host uses). `clean_file` returns
  the cleaned bytes as an `EmbeddedResourceBlock`
  (`BlobResourceContents`, base64-encoded with the correct MIME
  type); `clean_image` returns the cleaned PNG as an
  `ImageContentBlock`; everything else returns `TextContentBlock`
  (cleaned text or a JSON sidecar of `WatermarkMatch` / `AiArtifact`
  / `MetadataEntry` records). New `AddWatermarkRemoverMcp` extension
  in `DependencyInjection.cs` calls `AddMcpServer()` with the standard
  `serverInfo.name` / `serverInfo.version` (sourced from
  `ServerInfo` constants) and `.WithToolsFromAssembly()` for
  attribute-based discovery. Transport binding (stdio or Streamable
  HTTP) is deliberately left to the host — the next tick (`serve-mcp`
  CLI command, WR-S11) wires `WithStdioServerTransport()` or
  `WithHttpTransport()`. New `WatermarkRemover.Mcp.Tests` project
  with 28 tests across 9 fixtures: one happy-path + null-guard +
  error-case per tool (8 × 3 = 24), plus 4 DI extension tests
  asserting that `AddWatermarkRemoverMcp` returns a usable
  `IMcpServerBuilder`, that the SDK's `McpServerOptionsSetup` carries
  our `ServerInfo` values into the registered
  `IOptions<McpServerOptions>`, that exactly 8 `McpServerTool`
  services are discoverable with the expected names, and that all
  four pipeline interfaces the tools depend on resolve from the same
  container. Image and file tests use local fakes (no ONNX model,
  no network). `ModelContextProtocol` 2.2.0 + supporting
  `Microsoft.Extensions.*` 10.0.x added to
  `Directory.Packages.props`. New project added to
  `WatermarkRemover.sln`. Build clean (0 warnings, 0 errors);
  235 tests total, all green.
- **`serve-mcp` CLI command + `mcp:` config** — new
  `watermarkremover serve-mcp` command (WR-S11) hosts the MCP server
  in two transports, selected via `--transport`:
  - **`stdio`** (default) — local agent integration. Uses
    `Host.CreateApplicationBuilder()` + `AddWatermarkRemoverMcp()` +
    `.WithStdioServerTransport()`. All logging is routed to **stderr**
    via `LogToStandardErrorThreshold = LogLevel.Trace` so the
    JSON-RPC stream on stdout stays clean, per the [MCP stdio
    spec](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio).
  - **`http`** — Streamable HTTP transport (stateless, the SDK
    default). Uses `WebApplication.CreateBuilder()` +
    `AddWatermarkRemoverMcp()` + `.WithHttpTransport(o => o.Stateless = true)`
    + `app.MapMcp()`. Reuses the same `X-API-Key` middleware and
    per-IP rate-limit pattern as the regular `serve` command
    (defaults: `--port 5090`, `100 req/min/IP`, API key off).
    The `ModelContextProtocol.AspNetCore` 2.2.0 package is added
    to `Directory.Packages.props` and the CLI csproj.
  CLI flags: `--transport <stdio|http>`, `-H|--host`,
  `-p|--port` (default 5090 — distinct from `serve`'s 5080 so the
  two commands can run side by side), `--api-key`, `--rate-limit`,
  `--rate-window`. New `mcp:` section in `config.yaml` carries
  `transport` (default `stdio`), `host`, `port`, `api_key`, and
  `rate_limit.{permit_limit,window_seconds,queue_limit}`. The MCP
  rate-limit inherits `server.rate_limit` when not set explicitly.
  New `McpConfig` (with `Transport`, `Host`, `Port`, `ApiKey`,
  `RateLimit`) on `AppConfig`, plus `McpTransport` enum and
  `McpTransportExtensions.Parse` for case-insensitive string → enum
  mapping (accepts `stdio`/`pipe` and `http`/`streamable`/
  `streamable-http`; unknown values fail with a clear error). 43 new
  tests across 2 new fixtures + 1 new file: `McpConfigTests` (6
  tests, default-value invariants), `McpTransportParseTests` (15
  tests, every supported spelling + typo-rejection + round-trip), and
  `ServeMcpCommandTests` (22 tests, settings defaults + pre-flight
  validation + HTTP transport end-to-end via `TestServer` with
  initialize / tools-list / API-key on/off / health-endpoint
  exempt). `serve-mcp` added to the README commands table and
  shell-completion script catalogue; new `🤖 MCP server` README
  section with the Claude Code one-liner. 278 tests total, all green;
  build clean (0 warnings, 0 errors).
- **`watermarkremover --version` (and `-V`)** — new global short-circuit
  flag that prints `watermarkremover <assembly version>` and exits `0`
  *before* `config.yaml` is loaded, Serilog is wired up, or the DI
  container is built. Backed by the new
  `WatermarkRemover.CLI.Infrastructure.CliShortCircuits` helper (which
  reads `VersionInfo.Current` — sourced from
  `AssemblyInformationalVersionAttribute` on the entry assembly) and
  the new `<Version>1.0.0</Version>` / `<InformationalVersion>1.0.0</
  InformationalVersion>` properties on the CLI csproj. Three call
  shapes are accepted: `--version` (long form), `-V` (uppercase
  short), and a bare `-v` (lowercase short, only when it is the only
  arg). When `-v` is paired with another token it stays attached to
  the existing `--verbose` logging flow. 12 new tests in
  `VersionInfoTests` / `CliShortCircuitsTests` cover: the version
  value is never empty or whitespace-padded, the fallback string is
  stable, all three call shapes exit `0` and write
  `watermarkremover {version}`, the short-circuit still fires when
  mixed with other args, non-version invocations (and `-v` paired
  with another arg) fall through to the regular `CommandApp` path,
  and `null` args throw. README "Global options" table updated.
- **`POST /detect/markdown` endpoint** — new HTTP route in
  `ServeEndpointMapper.cs` (consumed by `serve`) that takes the same
  `{ markdown, stripAll }` body as `POST /clean/markdown` and returns
  `AiArtifact[]` — the same detector output the `detect-markdown` CLI
  command prints (frontmatter, AI signature, boilerplate disclaimer,
  invisible box-drawing separators, invisible characters inside code
  blocks). `stripAll` is intentionally ignored: detection uses a fixed
  detector set, not the cleaning toggles. 400 on empty body, 401 when
  `--api-key` is set and `X-API-Key` is missing (consistent with the
  rest of the API). 4 new tests in `HttpEndpointTests` cover the happy
  path (frontmatter + AI signature both reported), 400 on empty body,
  401 when auth is required, and that the new route is listed in
  `/swagger/v1/swagger.json`. README HTTP API table updated.
- **Full markdown config surface** — every public toggle on
  `MarkdownCleanOptions` is now reachable from `config.yaml`. The
  previous 12-key surface grew to all 21 (`strip_bold_italic`,
  `strip_blockquotes`, `strip_hr`, `strip_comments`, `strip_task_lists`,
  `strip_table_syntax`, `normalize_lists`, `unwrap_empty_lists`,
  `strip_xml_tags`, `apply_unicode_layer_a`); the legacy
  `preserve_code_blocks` knob is kept for backward compatibility. New
  `MarkdownCleanOptions.From(MarkdownConfig)` static factory in
  `WatermarkRemover.Core.Models` is the single source of truth for the
  binding — `clean-markdown`, `clean-all`, and the HTTP
  `POST /clean/markdown` endpoint all consume it. `clean-markdown` now
  reads the full config baseline; CLI flags (`--strip-all`,
  `--strip-code-fences`, `--strip-links`) override on a per-key basis.
  `docs/CONFIGURATION.md` documents every key with its default and
  one-line description. 30 new tests in `MarkdownConfigTests` and
  `ConfigYamlMarkdownTests` cover: every public boolean on
  `MarkdownCleanOptions` is surfaced in `MarkdownConfig`, the
  `MarkdownConfig` defaults equal the `MarkdownCleanOptions` defaults
  (so deleting a key from `config.yaml` is a no-op), `From()` round-trips
  every toggle, `--strip-all` enables every toggle, and a smoke test
  loads `src/config.yaml` and asserts all 21 keys are present.
- **Shell completion scripts** — new `completions --shell <bash|zsh|powershell|fish>`
  CLI command emits a static completion script for the requested shell,
  covering every sub-command and a curated set of common flags per
  command. Install with
  `watermarkremover completions --shell bash | sudo tee /etc/bash_completion.d/watermarkremover > /dev/null`
  (bash), drop the zsh script into `site-functions/_watermarkremover`,
  append the PowerShell block to `$PROFILE.CurrentUserAllHosts`, or
  redirect the fish script into `~/.config/fish/completions/`. See
  [`docs/SHELL-COMPLETION.md`](./docs/SHELL-COMPLETION.md) for the
  full install matrix. Static generator lives in
  `ShellCompletionScripts`; command list and option hints are
  kept in one place so all four shells stay in lockstep. 17 new
  tests in `CompletionsCommandTests` cover: every shell renders a
  script that names the main sub-commands, the bash script registers
  `complete -F _watermarkremover`, the zsh script starts with
  `#compdef watermarkremover`, the PowerShell script uses
  `Register-ArgumentCompleter`, the fish script uses the
  `complete` builtin, an unknown shell throws
  `ArgumentException`, and the command itself returns 1 on an
  unknown / empty shell and 0 with a valid script on stdout.
- **HTTP endpoint tests for `serve`** — the eight endpoints
  (`/health`, `POST /clean/text`, `POST /clean/markdown`,
  `POST /clean/file`, `POST /clean/image`, `POST /detect/text`,
  `POST /detect/image`, `POST /inspect/file`) plus the Swagger UI, the
  OpenAPI JSON, and the `wwwroot/` static-UI mount are now exercised
  end-to-end via `Microsoft.AspNetCore.TestHost`
  (`Microsoft.AspNetCore.Mvc.Testing 10.0.11`). 11 new tests in
  `HttpEndpointTests` cover: `/health` returns 200 + `{"status":"ok"}`;
  `/clean/text` strips U+200B (Layer A); `/clean/text` returns 400
  (`INVALID_INPUT`) on empty body; `/clean/text` returns 401 when
  `--api-key` is set and `X-API-Key` is missing; `/clean/text` returns
  200 with the right key; `/health` stays open even with auth on;
  `/swagger/index.html` returns the Swashbuckle UI HTML;
  `/swagger/v1/swagger.json` returns the OpenAPI 3.0 spec; `GET /`
  returns 200 with the bundled UI when `wwwroot/index.html` is shipped;
  `GET /` returns 404 when `--no-ui` is set; `GET /` returns 404 when
  `wwwroot/` is absent. The eight endpoints, the Swagger mount, and
  the static-UI mount were extracted out of `ServeCommand` into a new
  public static `ServeEndpointMapper` (with `MapEndpoints`,
  `MountSwagger`, and `MountStaticUi` helpers) so the test host can
  spin them up in-memory without binding Kestrel. `TextRequest` and
  `MarkdownRequest` are now top-level types in
  `WatermarkRemover.CLI.Commands` so the mapper (and tests) can
  reference them. 138 tests total, all green; 0 build warnings.
- **`clean-all` CLI command** — new `watermarkremover clean-all <path>`
  dispatches a single file or directory to the right pipeline per file:
  `.md`/`.markdown` → markdown cleaner, router-supported extensions
  (JPEG/PNG/PDF/DOCX/HTML/WebP) → metadata cleaner, `.txt`/`.text`/`.log`
  and no-extension files → text pipeline (Layers A/B/C). Unknown binary
  extensions (`.bin`, `.exe`, `.mp4`, …) are skipped with a warning
  rather than being fed to the text pipeline by mistake. Supports
  `--recursive` and `--dry-run`; honours the global `--json` flag. New
  `CleanAllClassifier` (pure routing decision) +
  `CleanAllCommand`; new `WatermarkRemover.CLI.Tests` project (the
  foundation for WR-S4) with 33 tests covering the classifier (markdown
  / router / text / no-extension / binary / null guards / ordering) and
  the full command (happy path on a mixed dir, recursive flag, dry-run,
  single-file mode, binary-skip, missing path). 127 tests total, all
  green.
- **File size limit enforcement** — multipart uploads to `/clean/file`,
  `/clean/image`, `/inspect/file`, and `/detect/image` are now rejected
  with HTTP 413 (`PAYLOAD_TOO_LARGE`) *before* the body is streamed to
  disk when they exceed the configured size cap. New `server.max_upload_mb`
  key in `config.yaml` (default `100`); CLI override `serve --max-upload-mb
  <n>`. `0` disables the limit (local dev only). Kestrel's
  `MaxRequestBodySize` is lifted to the configured limit so the
  framework-level 413 doesn't fire first — the structured `ErrorResult`
  JSON is always returned. New `MaxUploadMBTests` (11 tests) + 2
  `AppConfigTests` additions.
- **Configurable HTTP rate-limit** — the per-IP fixed-window limiter that
  used to be hard-coded at 100 requests / minute in `ServeCommand` is now
  driven by the new `server.rate_limit` section in `config.yaml`
  (`permit_limit`, `window_seconds`, `queue_limit`). Two new CLI flags
  override individual values at start-up: `serve --rate-limit <n>` and
  `serve --rate-window <seconds>`. Resolution order: CLI flag >
  `config.yaml` > built-in default (100 / 60 / 0). The active values and
  the source of the resolution are printed at start-up. Invalid values
  (`<= 0`) cause `serve` to exit with status `1` before binding sockets.
- **OpenAPI / Swagger UI** — interactive API documentation at
  `/swagger/` and the machine-readable OpenAPI 3.0 spec at
  `/swagger/v1/swagger.json`. All 8 HTTP endpoints are documented with
  request/response schemas, and the `X-API-Key` requirement is declared
  in the security section. Generated by `Swashbuckle.AspNetCore 10.1.1`
  against the live endpoint map; `Microsoft.OpenApi` is pinned to
  `2.7.5` to avoid the GHSA-v5pm-xwqc-g5wc (CVE-2026-49451) DoS
  advisory that affects the 2.0.0-preview.11 → 2.7.4 line.
- **Out-of-the-box experience** — every install path now produces a working
  API **and** web UI on the same port without extra steps:
  - The release pipeline (`.github/workflows/release.yml`) builds the Astro
    web UI in a new Node 22 step before `dotnet publish`, and the publish
    uses `IncludeAllContentForSelfExtract=true` so the static bundle is
    embedded in the single-file binary.
  - A new `Makefile` (Linux / macOS) and `scripts/build.ps1` (Windows
    PowerShell) wrap the whole flow in one command — `make build`,
    `make serve`, `make test`, or `scripts\build.ps1 -Serve`.
  - The Dockerfile `webbuild` stage (added with the UI itself) was already
    doing the same for Docker; the README and `docs/WEB-UI.md` now document
    it explicitly under "Out of the box".
- **Web UI (Astro "box")** — single-page plug-and-play dashboard at `/` with
  Text / Markdown / File / Image tabs. Built with Astro 5.x (`output: 'static'`,
  no UI framework, code-split per tab, <50 KB total page weight gzipped).
  Configured via two env vars (`PUBLIC_API_URL`, `PUBLIC_API_KEY`). Co-located
  with the .NET binary via `UseStaticFiles`; standalone deploys (Vercel /
  Netlify / GH Pages / nginx) also supported. See
  [`docs/WEB-UI.md`](./docs/WEB-UI.md).
- **`/web` Astro project** — `package.json`, `astro.config.mjs`, `tsconfig.json`,
  Tab components, vanilla-JS widgets, 20+ Vitest unit tests, `npm run build`
  that syncs `dist/` → `src/WatermarkRemover.CLI/wwwroot/`.
- **CORS middleware** in `ServeCommand` — `--cors-origins` flag +
  `WATERMARKREMOVER_CORS_ORIGINS` env var, smart defaults (`*` for open APIs,
  local dev hosts when `--api-key` is set).
- **`--no-ui` flag** in `ServeCommand` — skip serving `wwwroot/` for
  headless API-only deployments.
- **`OutputFormatter.Info`** — small additional method on the Spectre.Console
  helper for informational messages.
- **`docs/WEB-UI.md`** — comprehensive guide: dev loop, build, env vars,
  co-located serve, standalone deploy recipes, security notes, troubleshooting.
- **Multi-stage Docker build** — new `webbuild` stage (Node 22 alpine) that
  builds the Astro bundle and overlays it into the .NET source tree before
  `dotnet publish`.
- **`Makefile`** — `make`, `make build`, `make web`, `make dotnet`, `make test`,
  `make serve`, `make clean`, `make smoke` targets for Linux / macOS.
- **`scripts/build.ps1`** — one-command equivalent for Windows PowerShell.
  Handles npm deprecation warnings that otherwise trip `$ErrorActionPreference
  = 'Stop'`. Supports `-SkipWeb`, `-SkipDotnet`, `-Configuration`, `-Serve`,
  `-Port` switches.
- **Documentation overhaul** — comprehensive [README](./README.md) with badges, hero,
  TOC, install methods (binary / Docker / from source), comparison table, FAQ,
  Russian-language support section.
- **[CONTRIBUTING.md](./CONTRIBUTING.md)** — development environment, coding
  conventions, commit-message format (Conventional Commits), PR checklist,
  guides for adding new vendor detectors and metadata cleaners.
- **[SECURITY.md](./SECURITY.md)** — responsible-disclosure policy,
  supported-versions table, **responsible-use disclaimer**, threat model, HTTP
  server hardening notes.
- **[CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md)** — Contributor Covenant v2.1.
- **Issue templates** — `.github/ISSUE_TEMPLATE/bug-report.yml`,
  `feature-request.yml`, `config.yml`.
- **PR template** — `.github/PULL_REQUEST_TEMPLATE.md`.
- **[CODEOWNERS](./.github/CODEOWNERS)** — `@techbuzzz` owns all source paths.
- **[Dependabot config](./.github/dependabot.yml)** — weekly NuGet + GitHub
  Actions updates, with auto-merge for `patch` updates.
- **[FUNDING.yml](./.github/FUNDING.yml)** — funding links (GitHub Sponsors).
- **[docker-compose.yml](./docker-compose.yml)** — single-service dev compose
  with `./models` volume, optional API-key env var.
- **New docs** — [`docs/FAQ.md`](./docs/FAQ.md),
  [`docs/COMPARISON.md`](./docs/COMPARISON.md),
  [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md),
  [`docs/CONFIGURATION.md`](./docs/CONFIGURATION.md).
- **Russian-language synonyms** — extended built-in `SynonymDictionary`
  coverage (see `src/WatermarkRemover.Text/SynonymDictionary.cs`).

### Changed
- **Central package management (NuGet CPM)** — package versions are now
  declared in one place. New `src/Directory.Packages.props` lists every
  external package (Serilog, Spectre.Console, SixLabors.ImageSharp,
  OnnxRuntime, Swashbuckle, xunit, etc.); `Version="..."` is removed
  from every `<PackageReference>` in the 7 csprojs. Central management
  is activated via `<ManagePackageVersionsCentrally>true</
  ManagePackageVersionsCentrally>` in `Directory.Build.props`. Bumping
  a dependency now means editing one line. Verified: stray `Version`
  attributes correctly raise `NU1008`; build still 0 warnings; 70/70
  tests pass.
- README now uses badges, emoji-led section headings, and a TOC for faster
  navigation.

### Security
- Documented responsible-use policy, threat model, and HTTP-server hardening in
  [SECURITY.md](./SECURITY.md).

---

## [0.1.0] — 2026-08-21

> *Pre-release. The first tagged release will be cut from this state.*

### Added
- **Core pipeline** — `WatermarkRemover.Core` models, interfaces, configuration,
  DI contracts.
- **Text cleaning (three layers)** — Unicode hygiene, statistical green-list
  rewrite, vendor heuristics (Claude / Gemini / OpenAI).
- **Markdown cleaning** — 20+ toggleable transforms, fenced code-block
  preservation, AI-signature removal.
- **Metadata cleaners** — JPEG (segment parser), PNG (chunk filter),
  PDF (PdfPig rebuild), DOCX (OpenXML properties + revisions),
  HTML (meta + comments).
- **Image cleaning** — auto mask generation (alpha + colour-frequency
  heuristics) + LaMa ONNX inpainting; manual mask support; graceful
  degradation when the model is missing.
- **CLI** — Spectre.Console commands:
  `clean-text`, `clean-markdown`, `clean-file`, `clean-image`,
  `detect-text`, `detect-markdown`, `detect-watermark`, `inspect-file`,
  `download-model`, `serve`.
- **HTTP API** — ASP.NET Core Minimal API with `/health`, `POST /clean/text`,
  `/clean/markdown`, `/clean/file`, `/clean/image`, `/detect/text`,
  `/detect/image`, `/inspect/file`; rate-limited (100 req/min/IP); optional
  `X-API-Key` auth.
- **Configuration** — `src/config.yaml` with all layers and `Logging` knobs;
  resolution order documented.
- **Tests** — 62 tests across `WatermarkRemover.Text.Tests` (35),
  `WatermarkRemover.Metadata.Tests` (18), `WatermarkRemover.Image.Tests` (9).
- **CI** — `.github/workflows/build-and-test.yml` matrix on Windows + Linux
  with TRX + cobertura coverage artifacts.
- **Release pipeline** — `.github/workflows/release.yml` produces
  self-contained single-file binaries for `linux-x64`, `linux-arm64`,
  `win-x64`, `osx-x64` and attaches them to a GitHub Release.
- **Docker image** — multi-stage `Dockerfile` (alpine runtime, non-root user,
  `HEALTHCHECK`, `EXPOSE 5080`).
- **`global.json`** — pins the SDK to `10.0.400` (stable only).
- **`Directory.Build.props`** — common build conventions (warnings-as-errors,
  nullable, latest analyzers, `EnforceCodeStyleInBuild`).
- **Russian language support** — homoglyph-safe Unicode normalisation, built-in
  Russian synonym dictionary, dedicated unit tests in
  `StatisticalWatermarkRewriterTests` / `UnicodeHygieneCleanerTests` /
  `MarkdownCleanerTests` / `VendorDetectorTests`.

---

## Release history at a glance

| Tag       | Date       | Highlights                                                    |
|-----------|------------|---------------------------------------------------------------|
| (pending) | —          | First tagged release — see [Unreleased] above.                |

[Unreleased]: https://github.com/techbuzzz/ai-watermark-remover/compare/main...HEAD
[0.1.0]: https://github.com/techbuzzz/ai-watermark-remover/releases/tag/v0.1.0
