# 🧠 WatermarkRemover — Agent skills

Drop-in skill packages for AI coding assistants. Each skill is a folder
with a `SKILL.md` (the agent reads it) plus POSIX (`run.sh`) and
Windows (`run.ps1`) wrappers that pipe input through the
`watermarkremover` CLI.

| Skill | Folder | Purpose |
|-------|--------|---------|
| `watermark-clean-text`     | [`clean-text/`](./clean-text/)     | Strip invisible characters and rewrite AI-sounding phrasing in plain text (EN + RU). |
| `watermark-clean-markdown` | [`clean-markdown/`](./clean-markdown/) | Strip AI signatures / frontmatter from Markdown while preserving fenced code blocks. |
| `watermark-clean-file`     | [`clean-file/`](./clean-file/)     | Strip EXIF / XMP / IPTC / C2PA / XMP-pdf / DOCX core props / HTML meta from a file. Pixel-preserving. |
| `watermark-clean-image`    | [`clean-image/`](./clean-image/)    | Detect a visual watermark and inpaint over it with the LaMa ONNX model. |
| `watermark-detect`         | [`detect/`](./detect/)             | Read-only AI provenance check for text, markdown, and image. |

## Install

Pick the agent you want the skills installed for:

```bash
# POSIX
./install.sh --agent opencode        # project-local .opencode/skills/watermarkremover/
./install.sh --agent claude          # ~/.claude/skills/watermarkremover/
./install.sh --agent minimax         # ~/.minimax/skills/watermarkremover/
./install.sh --agent generic         # ~/.config/watermarkremover/skills/
./install.sh --agent auto            # probe CWD for .opencode/.claude/.minimax

# Windows PowerShell
.\install.ps1 -Agent opencode
.\install.ps1 -Agent claude
.\install.ps1 -Agent auto

# Dry run — print what would happen without touching anything
./install.sh --agent opencode --dry-run
```

The installer copies each `skills/<name>/` folder verbatim into
`<target>/watermarkremover/<name>/`. It overwrites any prior install at
the same path (after printing a warning).

The directory resolution rules are unit-tested in C# — see
[`src/tests/WatermarkRemover.CLI.Tests/SkillsInstallerTargetResolverTests.cs`](../src/tests/WatermarkRemover.CLI.Tests/SkillsInstallerTargetResolverTests.cs)
and the source of truth at
[`src/WatermarkRemover.CLI/Infrastructure/SkillsInstallerTargetResolver.cs`](../src/WatermarkRemover.CLI/Infrastructure/SkillsInstallerTargetResolver.cs).

## Layout

```
skills/
├── README.md                    # this file
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
├── install.sh                   # POSIX installer
└── install.ps1                  # Windows installer
```

## Reference

- 📘 [docs/SKILLS.md](../docs/SKILLS.md) — full reference, troubleshooting, MCP tool mapping.
- 🤖 [docs/MCP.md](../docs/MCP.md) — `serve-mcp` for Claude Code, OpenCode, MiniMax Code, Cursor, Continue.
