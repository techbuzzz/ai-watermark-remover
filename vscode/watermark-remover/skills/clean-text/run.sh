#!/usr/bin/env sh
# run.sh — wrapper for the `watermark-clean-text` skill.
#
# Reads text on stdin (or the first argument), pipes it through
# `watermarkremover clean-text --stdin`, and prints the cleaned text
# on stdout. The cleanup report is printed to stderr.
#
# Usage:
#   echo "Hello World" | ./run.sh
#   ./run.sh "Hello World"
#   ./run.sh --statistical < input.txt
#
# Recognised flags (forwarded to the CLI):
#   --statistical   enable Layer B (synonym rewrite)
#   --vendors       enable Layer C (vendor-specific rewrites)
#   --no-unicode    skip Layer A (Unicode hygiene)
#   --json          emit the full {cleaned, removed[]} JSON

set -eu

if ! command -v watermarkremover >/dev/null 2>&1; then
    echo "watermark-clean-text: 'watermarkremover' CLI not found on PATH" >&2
    echo "  install: https://github.com/techbuzzz/ai-watermark-remover" >&2
    exit 127
fi

if [ "$#" -eq 0 ] || [ "$1" = "-" ]; then
    watermarkremover clean-text --stdin "$@"
else
    watermarkremover clean-text "$@"
fi
