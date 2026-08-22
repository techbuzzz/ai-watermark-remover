// extension.ts — WatermarkRemover VS Code extension entry point
// =====================================================================
//
// This extension is a **thin UI layer** over the `watermarkremover` CLI.
// It does not re-implement any cleaning logic — every command spawns the
// `watermarkremover` binary as a child process and pipes data through it.
//
// Why CLI and not MCP? The CLI works out of the box as long as the
// `watermarkremover` binary is on `$PATH`. MCP registration in VS Code is
// newer (1.86+) and requires additional setup. The CLI path also works in
// restricted environments (Remote-SSH, dev containers) without any extra
// plumbing. A future version may route through the MCP server when
// `watermarkremover.preferMcp` is `true`; for now, CLI is the contract.
//
// Three commands are registered:
//   - `watermarkremover.cleanText`   — strip invisible / zero-width / homoglyph
//                                       characters and AI vendor patterns from
//                                       the current selection.
//   - `watermarkremover.cleanFile`   — strip EXIF / XMP / IPTC / C2PA metadata
//                                       from the file under the cursor in the
//                                       explorer.
//   - `watermarkremover.detectText`  — run detection only, return the list of
//                                       watermark matches as a JSON document
//                                       in a new editor.
//
// Two context-menu integrations:
//   - **editor/context** — when text is selected, show "Clean AI watermarks"
//     and "Detect AI watermarks".
//   - **explorer/context** — when a file is right-clicked, show
//     "Strip metadata".
//
// See README.md and `docs/VS-CODE.md` (in the parent repo) for the full
// configuration reference.

'use strict';

import * as vscode from 'vscode';
import { spawn } from 'node:child_process';

// ---- configuration helpers -------------------------------------------------

interface ExtensionConfig {
    binaryPath: string;
    preferMcp: boolean;
    statistical: boolean;
    showNotifications: boolean;
}

function readConfig(): ExtensionConfig {
    const cfg = vscode.workspace.getConfiguration('watermarkremover');
    return {
        binaryPath: cfg.get<string>('binaryPath', 'watermarkremover'),
        preferMcp: cfg.get<boolean>('preferMcp', false),
        statistical: cfg.get<boolean>('statistical', false),
        showNotifications: cfg.get<boolean>('showNotifications', true),
    };
}

// ---- child-process wrapper ------------------------------------------------

interface SpawnResult {
    stdout: string;
    stderr: string;
    code: number | null;
    signal: NodeJS.Signals | null;
}

interface SpawnOptions {
    /** When true, the child's stdin receives `input` and is closed on completion. */
    input?: string;
    /** When true, the result is parsed as JSON before returning. */
    parseJson?: boolean;
    /** Timeout in milliseconds (default 60 000 — 60s, matches the MCP server default). */
    timeoutMs?: number;
    /** Optional human-readable label for error messages. */
    label?: string;
}

class WatermarkRemoverNotFoundError extends Error {
    constructor(message: string) {
        super(message);
        this.name = 'WatermarkRemoverNotFoundError';
    }
}

class WatermarkRemoverFailedError extends Error {
    public readonly code: number | null;
    public readonly signal: NodeJS.Signals | null;
    public readonly stderr: string;
    constructor(message: string, code: number | null, signal: NodeJS.Signals | null, stderr: string) {
        super(message);
        this.name = 'WatermarkRemoverFailedError';
        this.code = code;
        this.signal = signal;
        this.stderr = stderr;
    }
}

/**
 * Spawn the `watermarkremover` binary with the given args and (optionally)
 * pipe `input` to its stdin. Returns stdout / stderr / exit code.
 *
 * Throws `WatermarkRemoverNotFoundError` if the binary can't be found or
 * the child fails to spawn. Throws `WatermarkRemoverFailedError` if the
 * child exits non-zero. The caller decides whether to surface these to
 * the user as a notification, an error message, or a fallback.
 */
async function runBinary(binaryPath: string, args: string[], opts: SpawnOptions = {}): Promise<SpawnResult> {
    return await new Promise<SpawnResult>((resolve, reject) => {
        let child;
        try {
            child = spawn(binaryPath, args, {
                stdio: ['pipe', 'pipe', 'pipe'],
                windowsHide: true,
            });
        } catch (err) {
            reject(new WatermarkRemoverNotFoundError(
                `Failed to spawn '${binaryPath}': ${(err as Error).message}. ` +
                `Set \`watermarkremover.binaryPath\` in VS Code settings to the full path, ` +
                `or install the binary from https://github.com/techbuzzz/ai-watermark-remover/releases.`,
            ));
            return;
        }

        const stdoutChunks: Buffer[] = [];
        const stderrChunks: Buffer[] = [];

        child.stdout.on('data', (chunk: Buffer) => { stdoutChunks.push(chunk); });
        child.stderr.on('data', (chunk: Buffer) => { stderrChunks.push(chunk); });

        let timedOut = false;
        const timeout = setTimeout(() => {
            timedOut = true;
            child.kill('SIGKILL');
        }, opts.timeoutMs ?? 60_000);

        child.on('error', (err: NodeJS.ErrnoException) => {
            clearTimeout(timeout);
            if (err.code === 'ENOENT') {
                reject(new WatermarkRemoverNotFoundError(
                    `Could not find the '${binaryPath}' binary on $PATH. ` +
                    `Install it from https://github.com/techbuzzz/ai-watermark-remover/releases ` +
                    `or set \`watermarkremover.binaryPath\` to an explicit path.`,
                ));
            } else {
                reject(new WatermarkRemoverNotFoundError(
                    `Failed to spawn '${binaryPath}': ${err.message}. ` +
                    `Set \`watermarkremover.binaryPath\` in VS Code settings to the full path.`,
                ));
            }
        });

        child.on('close', (code, signal) => {
            clearTimeout(timeout);
            if (timedOut) {
                reject(new WatermarkRemoverFailedError(
                    `'${binaryPath} ${args.join(' ')}' timed out after ${opts.timeoutMs ?? 60_000}ms (${opts.label ?? 'watermarkremover'}).`,
                    code, signal, Buffer.concat(stderrChunks).toString('utf8'),
                ));
                return;
            }
            const stdout = Buffer.concat(stdoutChunks).toString('utf8');
            const stderr = Buffer.concat(stderrChunks).toString('utf8');
            if (code !== 0) {
                reject(new WatermarkRemoverFailedError(
                    `'${binaryPath} ${args.join(' ')}' exited with code ${code}${signal ? ' (signal ' + signal + ')' : ''}: ${stderr.trim() || '(no stderr)'}`,
                    code, signal, stderr,
                ));
                return;
            }
            resolve({ stdout, stderr, code, signal });
        });

        // Pipe input to stdin if provided, then close stdin.
        if (opts.input !== undefined) {
            child.stdin.write(opts.input, 'utf8');
        }
        child.stdin.end();
    });
}

// ---- command: cleanText ----------------------------------------------------

async function cleanTextCommand(): Promise<void> {
    const cfg = readConfig();
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        void vscode.window.showErrorMessage('WatermarkRemover: no active text editor.');
        return;
    }

    const selection = editor.selection;
    if (selection.isEmpty) {
        void vscode.window.showErrorMessage('WatermarkRemover: select some text first (the command runs against the selection).');
        return;
    }

    const selected = editor.document.getText(selection);
    if (!selected) {
        void vscode.window.showErrorMessage('WatermarkRemover: selection is empty.');
        return;
    }

    const args = ['clean-text', '--stdin'];
    if (cfg.statistical) {
        args.push('--statistical');
    }

    try {
        const result = await runBinary(cfg.binaryPath, args, { input: selected, label: 'clean-text' });
        const cleaned = result.stdout;

        // Replace the selection with the cleaned text. VS Code's edit API
        // collapses the selection automatically.
        const success = await editor.edit((editBuilder) => {
            editBuilder.replace(selection, cleaned);
        });

        if (!success) {
            void vscode.window.showErrorMessage('WatermarkRemover: failed to apply the edit (the document may be read-only).');
            return;
        }

        if (cfg.showNotifications) {
            const removedChars = selected.length - cleaned.length;
            if (removedChars > 0) {
                void vscode.window.showInformationMessage(
                    `WatermarkRemover: removed ${removedChars} invisible / watermark character${removedChars === 1 ? '' : 's'}.`,
                );
            }
        }
    } catch (err) {
        handleError(err, 'cleanText');
    }
}

// ---- command: cleanFile ----------------------------------------------------

async function cleanFileCommand(uri?: vscode.Uri, uris?: vscode.Uri[]): Promise<void> {
    const cfg = readConfig();
    const targets: vscode.Uri[] = [];
    if (uris && uris.length > 0) {
        targets.push(...uris);
    } else if (uri) {
        targets.push(uri);
    } else {
        // Fall back to the active editor's file.
        const editor = vscode.window.activeTextEditor;
        if (editor) {
            targets.push(editor.document.uri);
        }
    }
    if (targets.length === 0) {
        void vscode.window.showErrorMessage('WatermarkRemover: no file selected (right-click a file in the Explorer, or open one in the editor).');
        return;
    }

    let successCount = 0;
    let skippedCount = 0;
    const failures: string[] = [];

    for (const target of targets) {
        // Only handle real on-disk files; refuse untitled / virtual docs.
        if (target.scheme !== 'file') {
            skippedCount++;
            continue;
        }

        try {
            await runBinary(cfg.binaryPath, ['clean-file', target.fsPath], { label: 'clean-file' });
            successCount++;
        } catch (err) {
            if (err instanceof WatermarkRemoverFailedError) {
                // Exit code 3 in `clean-file` means "unsupported format" — not a hard error.
                if (err.code === 3) {
                    skippedCount++;
                } else {
                    failures.push(`${vscode.workspace.asRelativePath(target)}: ${err.message}`);
                }
            } else {
                failures.push(`${vscode.workspace.asRelativePath(target)}: ${(err as Error).message}`);
            }
        }
    }

    if (cfg.showNotifications) {
        const summary: string[] = [];
        if (successCount > 0) {
            summary.push(`cleaned ${successCount} file${successCount === 1 ? '' : 's'}`);
        }
        if (skippedCount > 0) {
            summary.push(`skipped ${skippedCount} (unsupported format or non-file URI)`);
        }
        if (summary.length > 0) {
            void vscode.window.showInformationMessage(`WatermarkRemover: ${summary.join(', ')}.`);
        }
        if (failures.length > 0) {
            void vscode.window.showErrorMessage(
                `WatermarkRemover: ${failures.length} file${failures.length === 1 ? '' : 's'} failed:\n${failures.join('\n')}`,
            );
        }
    }
}

// ---- command: detectText ---------------------------------------------------

async function detectTextCommand(): Promise<void> {
    const cfg = readConfig();
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        void vscode.window.showErrorMessage('WatermarkRemover: no active text editor.');
        return;
    }

    const selection = editor.selection;
    if (selection.isEmpty) {
        void vscode.window.showErrorMessage('WatermarkRemover: select some text first.');
        return;
    }

    const selected = editor.document.getText(selection);
    if (!selected) {
        void vscode.window.showErrorMessage('WatermarkRemover: selection is empty.');
        return;
    }

    try {
        const result = await runBinary(
            cfg.binaryPath,
            ['detect-text', '--stdin', '--json'],
            { input: selected, label: 'detect-text' },
        );
        const parsed = JSON.parse(result.stdout) as unknown;
        const matches = Array.isArray(parsed) ? parsed : [];

        // Open the result in a new untitled document so the user can review
        // the matches without leaving the editor.
        const doc = await vscode.workspace.openTextDocument({
            content: JSON.stringify(matches, null, 2),
            language: 'json',
        });
        await vscode.window.showTextDocument(doc, { preview: false, viewColumn: vscode.ViewColumn.Beside });

        if (cfg.showNotifications) {
            if (matches.length === 0) {
                void vscode.window.showInformationMessage('WatermarkRemover: no AI watermarks detected in the selection.');
            } else {
                void vscode.window.showInformationMessage(
                    `WatermarkRemover: found ${matches.length} watermark match${matches.length === 1 ? '' : 'es'} (opened in a new tab).`,
                );
            }
        }
    } catch (err) {
        handleError(err, 'detectText');
    }
}

// ---- error handling --------------------------------------------------------

function handleError(err: unknown, command: string): void {
    if (err instanceof WatermarkRemoverNotFoundError) {
        // Binary not installed — give the user the install one-liner.
        const install = 'View install instructions';
        void vscode.window.showErrorMessage(
            `WatermarkRemover: ${err.message}`,
            install,
        ).then((choice) => {
            if (choice === install) {
                void vscode.env.openExternal(vscode.Uri.parse(
                    'https://github.com/techbuzzz/ai-watermark-remover/blob/main/docs/VS-CODE.md#install',
                ));
            }
        });
        return;
    }
    if (err instanceof WatermarkRemoverFailedError) {
        void vscode.window.showErrorMessage(
            `WatermarkRemover (${command}) failed: ${err.message}`,
        );
        return;
    }
    void vscode.window.showErrorMessage(
        `WatermarkRemover (${command}) failed: ${(err as Error).message ?? String(err)}`,
    );
}

// ---- activation ------------------------------------------------------------

export function activate(context: vscode.ExtensionContext): void {
    // Each command is registered as a disposable so VS Code can clean them up
    // when the extension deactivates. The wrappers forward the (optional)
    // URI list passed by `explorer/context` to the implementation.
    context.subscriptions.push(
        vscode.commands.registerCommand('watermarkremover.cleanText', cleanTextCommand),
        vscode.commands.registerCommand('watermarkremover.cleanFile', (uri?: vscode.Uri, uris?: vscode.Uri[]) => {
            return cleanFileCommand(uri, uris);
        }),
        vscode.commands.registerCommand('watermarkremover.detectText', detectTextCommand),
    );

    // Diagnostic log — visible in the Extension Host output channel.
    const cfg = readConfig();
    console.log(`[watermarkremover] activated. binary='${cfg.binaryPath}', preferMcp=${cfg.preferMcp}, statistical=${cfg.statistical}`);
}

export function deactivate(): void {
    // Nothing to clean up — the child processes spawned by runBinary are
    // short-lived (single command) and the extension holds no persistent
    // state. The function is kept for symmetry with the VS Code API.
}
