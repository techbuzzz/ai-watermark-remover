#!/usr/bin/env node
/*
 * Emulate Docker's .dockerignore matcher against the repository tree and
 * verify that none of the files the real Dockerfile references would be
 * excluded by the .dockerignore.
 *
 * Run from the repo root:
 *     node scripts/verify-dockerignore.cjs
 */
'use strict';

const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..');
const DOCKERIGNORE = path.join(REPO_ROOT, '.dockerignore');

// Files the real Dockerfile needs in the build context
const REQUIRED = [
    'global.json',
    'Directory.Build.props',
    'src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj',
    'src/WatermarkRemover.Core/WatermarkRemover.Core.csproj',
    'src/WatermarkRemover.Text/WatermarkRemover.Text.csproj',
    'src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj',
    'src/WatermarkRemover.Image/WatermarkRemover.Image.csproj',
    'src/WatermarkRemover.sln',
];

function loadRules(file) {
    const text = fs.readFileSync(file, 'utf8');
    const out = [];
    for (const raw of text.split(/\r?\n/)) {
        const line = raw.trim();
        if (!line || line.startsWith('#')) continue;
        if (line.startsWith('!')) out.push({ negated: true, pattern: line.slice(1) });
        else out.push({ negated: false, pattern: line });
    }
    return out;
}

function escapeRegex(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function patternToRegex(pattern) {
    let p = pattern;
    const dirOnly = p.endsWith('/');
    if (dirOnly) p = p.slice(0, -1);
    const anchored = p.startsWith('/');
    if (anchored) p = p.slice(1);

    let re = '';
    let i = 0;
    while (i < p.length) {
        const c = p[i];
        if (c === '*') {
            if (p.slice(i, i + 3) === '**/') {
                re += '(?:.*/)?';
                i += 3;
            } else if (p[i + 1] === '*') {
                re += '[^/]*';
                i += 2;
            } else {
                re += '[^/]*';
                i += 1;
            }
        } else if (c === '?') {
            re += '[^/]';
            i += 1;
        } else if (c === '[' || c === ']') {
            // Preserve character class brackets (e.g. [Bb]) verbatim
            re += c;
            i += 1;
        } else if ('.^$+(){}|\\'.includes(c)) {
            re += '\\' + c;
            i += 1;
        } else {
            re += escapeRegex(c);
            i += 1;
        }
    }
    const body = '(?:^|/)' + re;
    return new RegExp(anchored ? '^' + re : body + (dirOnly ? '(?:/|$)' : '(?:$|/)'));
}

function compileRules(rules) {
    return rules.map(r => ({ ...r, regex: patternToRegex(r.pattern) }));
}

function isIgnored(rel, isDir, rules) {
    let ignored = false;
    for (const r of rules) {
        // Docker matches:
        //   - anchored patterns: only from the context root
        //   - patterns with `/`: anywhere in the path
        //   - patterns without `/`: basename match at any level
        const tryMatch = () => {
            if (r.regex.test(rel)) return true;
            if (!r.pattern.startsWith('/') && rel.includes('/')) {
                const base = rel.slice(rel.lastIndexOf('/') + 1);
                const re2 = new RegExp(
                    '(?:^|/)' + r.regex.source.replace(/^\(\?:(\^|\(\?:\^)\/\)??/, '(?:^|/)') + '(?:$|/)'
                );
                return re2.test(base) || new RegExp(r.regex.source + '$').test(base);
            }
            return false;
        };
        if (tryMatch()) ignored = !r.negated;
    }
    return ignored;
}

function walk(dir, cb) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            cb(full, true);
            if (entry.name !== '.git' && entry.name !== 'node_modules') {
                walk(full, cb);
            }
        } else {
            cb(full, false);
        }
    }
}

function main() {
    if (!fs.existsSync(DOCKERIGNORE)) {
        console.error(`ERROR: ${DOCKERIGNORE} not found`);
        process.exit(1);
    }
    const rules = compileRules(loadRules(DOCKERIGNORE));

    const failures = [];
    for (const rel of REQUIRED) {
        const abs = path.join(REPO_ROOT, rel);
        const exists = fs.existsSync(abs);
        const isDir = exists ? fs.statSync(abs).isDirectory() : false;
        const relNorm = rel.replace(/\\/g, '/');
        const ignored = isIgnored(relNorm, isDir, rules);
        const status = ignored ? 'EXCLUDED' : 'kept';
        const existence = exists ? 'exists' : 'missing';
        if (ignored && exists) failures.push(rel);
        console.log(`  [${status.padStart(7)}] (${existence}) ${rel}`);
    }

    console.log('\n--- Build context simulation ---');
    let kept = 0, keptBytes = 0, excl = 0, exclBytes = 0;
    walk(REPO_ROOT, (abs, isDir) => {
        const rel = path.relative(REPO_ROOT, abs).replace(/\\/g, '/');
        if (!rel) return;
        if (isIgnored(rel, isDir, rules)) {
            if (isDir) return; // do not descend into excluded dirs
            excl++; exclBytes += fs.statSync(abs).size;
        } else {
            if (!isDir) { kept++; keptBytes += fs.statSync(abs).size; }
        }
    });

    console.log(`  Kept    : ${String(kept).padStart(6)} files, ${(keptBytes / 1048576).toFixed(2).padStart(8)} MiB`);
    console.log(`  Excluded: ${String(excl).padStart(6)} files, ${(exclBytes / 1048576).toFixed(2).padStart(8)} MiB`);

    console.log();
    if (failures.length) {
        console.error('FAIL: required files would be excluded by .dockerignore:');
        for (const f of failures) console.error(`  - ${f}`);
        process.exit(2);
    }
    console.log('OK: no required files excluded.');
}

main();
