#!/usr/bin/env sh
# run.sh — wrapper for the `watermark-detect` skill.
#
# Reads text on stdin (or the file path / mode given as the first
# argument) and pipes it through the matching `detect-*` CLI command.
# Always emits the JSON report on stdout.
#
# Usage:
#   echo "text" | ./run.sh text
#   echo "text" | ./run.sh markdown
#   ./run.sh text -- file.txt
#   ./run.sh image photo.png
#
# Positional mode: text | markdown | image
# Default mode when none given: text

set -eu

if ! command -v watermarkremover >/dev/null 2>&1; then
    echo "watermark-detect: 'watermarkremover' CLI not found on PATH" >&2
    exit 127
fi

mode="${1:-text}"
shift 2>/dev/null || true

case "$mode" in
    text)
        watermarkremover detect-text --stdin "$@"
        ;;
    markdown)
        watermarkremover detect-markdown --stdin "$@"
        ;;
    image)
        watermarkremover detect-watermark "$@"
        ;;
    *)
        echo "watermark-detect: unknown mode '$mode' (use: text | markdown | image)" >&2
        exit 2
        ;;
esac
