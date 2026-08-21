# 🧠 Agent skills (`skills/`)

This document is the user-and-developer reference for the **drop-in
skill packages** that teach AI coding assistants when and how to use
WatermarkRemover. Each skill is a self-contained folder with a
`SKILL.md` (the agent reads this) plus POSIX (`run.sh`) and Windows
(`run.ps1`) wrappers that pipe input through the `watermarkremover`
CLI binary.

> **TL;DR.** `git clone` (or `git submodule add`) → `skills/install.sh
> --agent opencode` → restart the agent → done.

---

## What is a "skill"?

A skill is a small folder the agent looks for at startup. Most modern
agentic runtimes (OpenCode, Claude Code, MiniMax Code, Cursor,
Continue) follow the same convention: drop a `SKILL.md` (with YAML
frontmatter describing *when* to use the skill) into the agent's
skills directory, and the agent teaches itself to invoke it on user
prompts that match the description.

WatermarkRemover ships five skills that map 1-to-1 onto the
cleaning / detection surfaces of the CLI and MCP server:

| Skill                        | CLI command           | MCP tool             |
|------------------------------|-----------------------|----------------------|
| `watermark-clean-text`       | `clean-text`          | `clean_text`         |
| `watermark-clean-markdown`   | `clean-markdown`      | `clean_markdown`     |
| `watermark-clean-file`       | `clean-file`          | `clean_file`         |
| `watermark-clean-image`      | `clean-image`         | `clean_image`        |
| `watermark-detect`           | `detect-text` / `detect-markdown` / `detect-watermark` | `detect_text` / `detect_markdown` / `detect_watermark` |

The skill's `SKILL.md` is the agent's instruction manual; the `run.sh`
/ `run.ps1` wrappers are the implementation when MCP is not available
or the user prefers the CLI.

---

## Layout

```
skills/
├── README.md
├── clean-text/
│   ├── SKILL.md
│   ├── run.sh
│   └── run.ps1
├── clean-markdown/
│   ├── SKILL.md
│   ├── run.sh
│   └── run.ps1
├── clean-file/
│   ├── SKILL.md
│   ├── run.sh
│   └── run.ps1
├── clean-image/
│   ├── SKILL.md
│   ├── run.sh
│   └── run.ps1
├── detect/
│   ├── SKILL.md
│   ├── run.sh
│   └── run.ps1
├── install.sh
└── install.ps1
```

The installer (`install.sh` / `install.ps1`) is the single entry point
for getting the skills into the right directory.

---

## Install

### 1. Auto-detect (recommended)

```bash
# POSIX — probes CWD for .opencode/, .claude/, .minimax/ in that order.
./skills/install.sh

# Windows PowerShell
.\skills\install.ps1
```

If no project marker is found the installer falls back to `generic`
(`$HOME/.config/watermarkremover/skills/`), which is a safe default
for a user-global install.

### 2. Explicit agent

```bash
./skills/install.sh --agent opencode          # ./.opencode/skills/watermarkremover/
./skills/install.sh --agent claude            # ~/.claude/skills/watermarkremover/
./skills/install.sh --agent minimax           # ~/.minimax/skills/watermarkremover/
./skills/install.sh --agent cursor            # ~/.cursor/skills/watermarkremover/
./skills/install.sh --agent continue          # ~/.continue/skills/watermarkremover/
./skills/install.sh --agent generic           # ~/.config/watermarkremover/skills/
```

### 3. Explicit target (advanced)

```bash
./skills/install.sh --agent claude --target ./local-skills
```

When `--target` is given, it wins over every other resolution rule.

### 4. Dry run

```bash
./skills/install.sh --agent opencode --dry-run
```

Prints the resolution and the planned copies without writing anything.
Returns exit code `0` on a clean dry run; `1` if the resolution itself
fails (e.g. unknown agent name).

### 5. List known agents

```bash
./skills/install.sh --list
# auto
# claude
# claude-code
# opencode
# minimax
# minimax-code
# cursor
# continue
# generic
```

---

## Resolution rules (single source of truth)

The `skills/install.sh` and `skills/install.ps1` scripts share the
same resolution rules as the C# unit-tested
[`SkillsInstallerTargetResolver`](../src/WatermarkRemover.CLI/Infrastructure/SkillsInstallerTargetResolver.cs).
The matrix:

| Agent (`--agent …`)             | Resolved target dir (no overrides)                                |
|---------------------------------|-------------------------------------------------------------------|
| `auto`                          | `WATERMARKREMOVER_SKILLS_AGENT` → probe CWD markers → `generic`   |
| `claude` / `claude-code`        | `$HOME/.claude/skills/watermarkremover/`                          |
| `opencode`                      | `<cwd>/.opencode/skills/watermarkremover/`                        |
| `minimax` / `minimax-code`      | `$HOME/.minimax/skills/watermarkremover/`                         |
| `cursor`                        | `$HOME/.cursor/skills/watermarkremover/`                          |
| `continue`                      | `$HOME/.continue/skills/watermarkremover/`                        |
| `generic`                       | `$HOME/.config/watermarkremover/skills/` (or `<cwd>/skills/`)     |

**Project markers** probed under `auto` (first hit wins):

1. `./.opencode/` → `opencode`
2. `./.claude/`   → `claude`
3. `./.minimax/`  → `minimax`
4. otherwise      → `generic`

**Environment overrides** (any of them pins the corresponding agent's
target, no matter which `--agent` you passed):

| Variable                                   | Effect                              |
|--------------------------------------------|-------------------------------------|
| `WATERMARKREMOVER_SKILLS_AGENT`            | Pin the agent under `--agent auto`. |
| `WATERMARKREMOVER_SKILLS_CLAUDE_DIR`       | Override the claude target.         |
| `WATERMARKREMOVER_SKILLS_OPENCODE_DIR`     | Override the opencode target.       |
| `WATERMARKREMOVER_SKILLS_MINIMAX_DIR`      | Override the minimax target.        |
| `WATERMARKREMOVER_SKILLS_GENERIC_DIR`      | Override the generic target.        |
| `HOME` / `USERPROFILE`                     | User home dir (used by claude / minimax / cursor / continue / generic fallback). |

---

## What each skill does

### `watermark-clean-text`

Strips invisible characters (ZWSP, ZWJ, soft hyphen, Cyrillic / Latin
homoglyphs) and optionally rewrites AI-sounding phrasing (EN + RU
synonym dictionary). See [`skills/clean-text/SKILL.md`](../skills/clean-text/SKILL.md).

### `watermark-clean-markdown`

Strips YAML / TOML frontmatter, AI-signature em-dash patterns, and
invisible Unicode from a Markdown document — without touching fenced
code blocks. The 21 cleanup toggles are configurable through
`config.yaml`. See [`skills/clean-markdown/SKILL.md`](../skills/clean-markdown/SKILL.md).

### `watermark-clean-file`

Byte-level metadata stripper for JPEG, PNG, WebP, PDF, DOCX, and HTML.
Pixel-preserving for images (only EXIF / XMP / IPTC / C2PA / tEXt
chunks are removed). See [`skills/clean-file/SKILL.md`](../skills/clean-file/SKILL.md).

### `watermark-clean-image`

Detects a visual watermark region (or accepts a hand-painted mask PNG)
and inpaints it with the LaMa ONNX model. Requires the model to be
downloaded once (`watermarkremover download-model`, ~200 MB). See
[`skills/clean-image/SKILL.md`](../skills/clean-image/SKILL.md).

### `watermark-detect`

Read-only check — returns a `WatermarkMatch[]` report naming the
vendor (claude / gemini / openai / …), the kind (invisible-unicode /
token-bias / yaml-frontmatter / em-dash-frequency / signature-phrase),
and the evidence. Never modifies the input. See
[`skills/detect/SKILL.md`](../skills/detect/SKILL.md).

---

## MCP vs skill

The two are complementary, not redundant:

- **MCP** registers the tools (`clean_text`, `clean_markdown`,
  `clean_file`, `clean_image`, `detect_text`, `detect_markdown`,
  `inspect_file`, `detect_watermark`) so the agent can call them
  directly. See [`docs/MCP.md`](./MCP.md).
- **Skill** teaches the agent *when* to call those tools (or the
  equivalent CLI command) by matching user intent to the skill
  description in the `SKILL.md` frontmatter.

The recommended setup is **both**: register the MCP server (one-liner
per host in [`docs/MCP.md`](./MCP.md)) and install the skills folder
via `install.sh --agent <name>`. The skill triggers the MCP tool
automatically.

---

## Troubleshooting

| Symptom                                          | Cause / fix                                                                                  |
|--------------------------------------------------|----------------------------------------------------------------------------------------------|
| `watermarkremover: command not found`            | Install the CLI first. See [README → Installation](../README.md#-installation).               |
| Skill installs but the agent never calls it      | The agent's `SKILL.md` matcher needs more specific descriptions. Re-run with `--list` to see agent id, then re-install pointing at the right agent dir. |
| `./install.sh: unknown agent 'X'`                | Re-run `./install.sh --list` and pick one of the canonical names.                             |
| `Cannot resolve a home-relative … target`        | `HOME` / `USERPROFILE` is empty. Either set it or pass `--target <path>`.                     |
| Skills appear at the right path but the agent ignores them | Most agents cache the skill list at startup. Restart the agent after `install.sh`.   |
| `cp -R` not available                            | Use `install.ps1` on Windows. `cp` is the POSIX requirement.                                  |

---

## Reference

- 🧠 [skills/README.md](../skills/README.md) — top-level skill index.
- 🤖 [docs/MCP.md](./MCP.md) — the MCP server these skills wrap.
- ⚙️ [docs/CONFIGURATION.md](./CONFIGURATION.md) — every `config.yaml` key (the skills inherit the same defaults).
- 🏛️ [docs/ARCHITECTURE.md](./ARCHITECTURE.md) — the cleaning / detection pipeline the skills drive.
