#!/usr/bin/env sh
# run.sh — wrapper for the `watermark-clean-file` skill.
#
# Reads a file path as $1, pipes it through `watermarkremover clean-file`,
# and writes the cleaned file to stdout. Use `--output <path>` to write
# to disk instead.
#
# Usage:
#   ./run.sh photo.jpg > clean.jpg
#   ./run.sh photo.jpg --output clean.jpg
#   ./run.sh --inspect photo.jpg
#
# Recognised flags (forwarded to the CLI):
#   --inspect    call `inspect-file` and print the JSON report
#   --output     write the cleaned file to this path (default: stdout)
#   --strip-icc  also drop the ICC colour profile (off by default)

set -eu

if ! command -v watermarkremover >/dev/null 2>&1; then
    echo "watermark-clean-file: 'watermarkremover' CLI not found on PATH" >&2
    exit 127
fi

if [ "${1:-}" = "--inspect" ]; then
    shift
    watermarkremover inspect-file "$@"
else
    watermarkremover clean-file "$@"
fi
