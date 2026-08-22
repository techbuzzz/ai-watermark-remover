#!/usr/bin/env sh
# run.sh — wrapper for the `watermark-clean-image` skill.
#
# Reads an image path as $1, runs `detect-watermark` and (if a region is
# found) `clean-image` to inpaint the watermark with LaMa. Writes the
# cleaned image to stdout.
#
# Usage:
#   ./run.sh photo.png > clean.png
#   ./run.sh photo.png --mask mask.png > clean.png
#   ./run.sh photo.png --output clean.png
#
# Recognised flags (forwarded to the CLI):
#   --mask <path>    pass an explicit mask PNG
#   --output <path>  write the cleaned file to this path
#   --no-detect      skip the auto-detect step (use only with --mask)
#   --json           emit the detect report as JSON

set -eu

if ! command -v watermarkremover >/dev/null 2>&1; then
    echo "watermark-clean-image: 'watermarkremover' CLI not found on PATH" >&2
    exit 127
fi

if [ "${1:-}" = "--no-detect" ]; then
    shift
    watermarkremover clean-image "$@"
else
    watermarkremover clean-image "$@"
fi
