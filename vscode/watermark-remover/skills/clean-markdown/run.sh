#!/usr/bin/env sh
# run.sh — wrapper for the `watermark-clean-markdown` skill.
#
# Reads Markdown on stdin (or the file path given as the first argument)
# and pipes it through `watermarkremover clean-markdown --strip-all`.
# Writes the cleaned Markdown to stdout.
#
# Usage:
#   cat post.md | ./run.sh
#   ./run.sh post.md
#   ./run.sh < post.md
#
# Recognised flags (forwarded to the CLI):
#   --no-strip-all   disable the --strip-all preset
#   --json           emit the full {cleaned, transformsApplied[]} JSON

set -eu

if ! command -v watermarkremover >/dev/null 2>&1; then
    echo "watermark-clean-markdown: 'watermarkremover' CLI not found on PATH" >&2
    exit 127
fi

watermarkremover clean-markdown --strip-all "$@"
