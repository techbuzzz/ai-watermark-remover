#!/usr/bin/env sh
# install.sh — install the WatermarkRemover skills into a target agent's
# skills directory.
#
# Usage:
#   ./install.sh                                    # auto-detect agent
#   ./install.sh --agent opencode                   # explicit agent
#   ./install.sh --agent opencode --target ./skills # explicit target
#   ./install.sh --list                             # list known agents
#   ./install.sh --dry-run                          # print what would happen
#   ./install.sh --help
#
# The directory resolution rules are the same as the C# unit-tested
# SkillsInstallerTargetResolver (src/WatermarkRemover.CLI/Infrastructure/
# SkillsInstallerTargetResolver.cs). Keep them in sync.

set -eu

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
SKILL_SUBDIR="watermarkremover"
DRY_RUN=0
LIST=0
AGENT=""
TARGET_OVERRIDE=""

usage() {
    cat <<EOF
install.sh — install WatermarkRemover skills into a target agent.

USAGE
    ./install.sh [--agent NAME] [--target PATH] [--dry-run] [--list] [--help]

OPTIONS
    --agent NAME     One of: auto, claude, claude-code, opencode, minimax,
                     minimax-code, cursor, continue, generic. Default: auto.
    --target PATH    Override the resolved target directory.
    --dry-run        Print what would happen without touching the filesystem.
    --list           List the known agent names and exit.
    --help           Show this help and exit.

ENVIRONMENT
    WATERMARKREMOVER_SKILLS_AGENT         Pin the agent under --agent auto.
    WATERMARKREMOVER_SKILLS_CLAUDE_DIR    Override the claude target.
    WATERMARKREMOVER_SKILLS_OPENCODE_DIR  Override the opencode target.
    WATERMARKREMOVER_SKILLS_MINIMAX_DIR   Override the minimax target.
    WATERMARKREMOVER_SKILLS_GENERIC_DIR   Override the generic target.
    HOME / USERPROFILE                    User home directory.
EOF
}

log()  { printf '==> %s\n' "$*" >&2; }
warn() { printf 'warn: %s\n' "$*" >&2; }
die()  { printf 'error: %s\n' "$*" >&2; exit 1; }

# ---- argument parsing --------------------------------------------------

while [ $# -gt 0 ]; do
    case "$1" in
        --agent)   AGENT="$2"; shift 2 ;;
        --target)  TARGET_OVERRIDE="$2"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
        --list)    LIST=1; shift ;;
        --help|-h) usage; exit 0 ;;
        *) die "unknown argument: $1 (use --help)" ;;
    esac
done

if [ "$LIST" -eq 1 ]; then
    printf '%s\n' auto claude claude-code opencode minimax minimax-code cursor continue generic
    exit 0
fi

# ---- resolution ---------------------------------------------------------

home_dir() {
    if [ -n "${HOME:-}" ]; then
        printf '%s' "$HOME"
    elif [ -n "${USERPROFILE:-}" ]; then
        printf '%s' "$USERPROFILE"
    else
        return 1
    fi
}

probe_agent() {
    # If a project marker is visible from CWD, use it. Otherwise generic.
    if [ -d "./.opencode" ]; then printf 'opencode\n'; return; fi
    if [ -d "./.claude" ];   then printf 'claude\n';   return; fi
    if [ -d "./.minimax" ];  then printf 'minimax\n';  return; fi
    printf 'generic\n'
}

canonicalize_agent() {
    case "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" in
        ""|auto)            printf 'auto\n' ;;
        claude-code|claude) printf 'claude\n' ;;
        opencode)           printf 'opencode\n' ;;
        minimax-code|minimaxcode|minimax) printf 'minimax\n' ;;
        cursor)             printf 'cursor\n' ;;
        continue)           printf 'continue\n' ;;
        generic)            printf 'generic\n' ;;
        *) die "unknown agent '$1' (use --list to see known agents)" ;;
    esac
}

resolve_target() {
    _agent="$1"
    if [ -n "${WATERMARKREMOVER_SKILLS_AGENT:-}" ] && [ "$_agent" = "auto" ]; then
        _agent="$(canonicalize_agent "$WATERMARKREMOVER_SKILLS_AGENT")"
    fi
    if [ "$_agent" = "auto" ]; then
        _agent="$(probe_agent)"
    fi
    if [ -n "$TARGET_OVERRIDE" ]; then
        printf '%s\n' "$TARGET_OVERRIDE"
        return
    fi
    case "$_agent" in
        claude)
            if [ -n "${WATERMARKREMOVER_SKILLS_CLAUDE_DIR:-}" ]; then
                printf '%s\n' "$WATERMARKREMOVER_SKILLS_CLAUDE_DIR"
            else
                printf '%s/.claude/skills\n' "$(home_dir)"
            fi
            ;;
        opencode)
            if [ -n "${WATERMARKREMOVER_SKILLS_OPENCODE_DIR:-}" ]; then
                printf '%s\n' "$WATERMARKREMOVER_SKILLS_OPENCODE_DIR"
            else
                printf '%s/.opencode/skills\n' "$(pwd)"
            fi
            ;;
        minimax)
            if [ -n "${WATERMARKREMOVER_SKILLS_MINIMAX_DIR:-}" ]; then
                printf '%s\n' "$WATERMARKREMOVER_SKILLS_MINIMAX_DIR"
            else
                printf '%s/.minimax/skills\n' "$(home_dir)"
            fi
            ;;
        cursor)
            printf '%s/.cursor/skills\n' "$(home_dir)"
            ;;
        continue)
            printf '%s/.continue/skills\n' "$(home_dir)"
            ;;
        generic)
            if [ -n "${WATERMARKREMOVER_SKILLS_GENERIC_DIR:-}" ]; then
                printf '%s\n' "$WATERMARKREMOVER_SKILLS_GENERIC_DIR"
            else
                if HOME_DIR="$(home_dir)"; then
                    printf '%s/.config/watermarkremover/skills\n' "$HOME_DIR"
                else
                    printf '%s/skills\n' "$(pwd)"
                fi
            fi
            ;;
        *)
            die "internal: unhandled agent '$_agent'"
            ;;
    esac
}

canonical="$(canonicalize_agent "${AGENT:-auto}")"
target_root="$(resolve_target "$canonical")"
target="${target_root}/${SKILL_SUBDIR}"

# ---- preflight ---------------------------------------------------------

if ! command -v cp >/dev/null 2>&1; then
    die "cp not found on PATH"
fi
if [ ! -d "$SCRIPT_DIR" ]; then
    die "skills directory not found: $SCRIPT_DIR"
fi

SKILL_DIRS=""
for d in "$SCRIPT_DIR"/*/; do
    [ -d "$d" ] || continue
    name="$(basename "$d")"
    case "$name" in
        install.sh|install.ps1|README.md) continue ;;
    esac
    if [ -f "$d/SKILL.md" ]; then
        SKILL_DIRS="$SKILL_DIRS $name"
    fi
done

if [ -z "$SKILL_DIRS" ]; then
    die "no skill folders with SKILL.md found under $SCRIPT_DIR"
fi

log "agent      = $canonical"
log "target     = $target"
if [ "$DRY_RUN" -eq 1 ]; then
    log "dry-run    = yes (no filesystem changes)"
fi
log "skills     =$SKILL_DIRS"

# ---- install ------------------------------------------------------------

if [ "$DRY_RUN" -eq 0 ]; then
    mkdir -p "$target"
fi

for name in $SKILL_DIRS; do
    src="$SCRIPT_DIR/$name"
    dst="$target/$name"
    if [ "$DRY_RUN" -eq 1 ]; then
        log "would copy: $src -> $dst"
    else
        if [ -d "$dst" ]; then
            warn "overwriting existing skill at $dst"
            rm -rf "$dst"
        fi
        cp -R "$src" "$dst"
        log "copied     : $name"
    fi
done

if [ "$DRY_RUN" -eq 0 ]; then
    log "done. skills installed to $target"
else
    log "done. (dry-run) no files were written"
fi
