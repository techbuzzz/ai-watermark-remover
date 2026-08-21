#!/usr/bin/env node
// auto-clean.js — UserPromptSubmit hook that pipes the user's prompt
// through `watermarkremover clean-text --stdin` and injects the cleaned
// version as `hookSpecificOutput.additionalContext`.
//
// Claude Code fires UserPromptSubmit before every prompt. The hook
// receives JSON on stdin:
//
//   { session_id, transcript_path, cwd, hook_event_name, prompt, ... }
//
// We extract `.prompt`, run the CLI, and emit the JSON response shape
// on stdout. We intentionally do nothing (empty stdout) when:
//   - the prompt is empty,
//   - the CLI is missing (`spawnSync` errors),
//   - the CLI returns non-zero (avoids surfacing engine errors in chat),
//   - the cleaned output equals the input (avoids adding noise for
//     prompts that did not contain any invisible characters).
//
// That way the hook is invisible in the common case and only adds
// context when it actually changed something.
//
// Reference: https://code.claude.com/docs/en/hooks

'use strict';

const { spawnSync } = require('child_process');

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { raw += chunk; });
process.stdin.on('end', () => {
  let prompt = '';
  try {
    const input = JSON.parse(raw || '{}');
    if (typeof input.prompt === 'string') {
      prompt = input.prompt;
    }
  } catch (_) {
    // Malformed input — treat as empty.
  }

  if (!prompt) {
    return; // Nothing to clean; emit no context.
  }

  const result = spawnSync(
    'watermarkremover',
    ['clean-text', '--stdin'],
    { input: prompt, encoding: 'utf8', timeout: 20000 }
  );

  if (result.error || result.status !== 0) {
    return; // CLI missing or failed — silently skip.
  }

  const cleaned = (result.stdout || '').replace(/^\s+|\s+$/g, '');
  if (!cleaned || cleaned === prompt) {
    return; // No change; don't add noise.
  }

  const response = {
    hookSpecificOutput: {
      hookEventName: 'UserPromptSubmit',
      additionalContext:
        '[watermarkremover auto-clean] The pasted text contained ' +
        'invisible / zero-width / homoglyph characters. Cleaned version:\n\n' +
        cleaned,
    },
  };
  process.stdout.write(JSON.stringify(response));
});
