// test/extension.test.js — structural tests for the WatermarkRemover VS Code
// extension package. Verifies that the package.json manifest is well-formed
// and contains the fields the VS Code marketplace + the runtime need.
//
// Run with: `npm test` (uses `node --test`, no external deps).
//
// The behavioural tests for the extension (the actual command implementations
// in `src/extension.ts`) live in the parent repo's xUnit suite under
// `src/tests/WatermarkRemover.CLI.Tests/VsCodeExtensionTests.cs`. Those
// tests verify the static contract; this Node suite is a quick smoke
// test that anyone cloning the extension alone can run.

'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const pkg = require('../package.json');

const REQUIRED_TOP_FIELDS = [
    'name',
    'displayName',
    'description',
    'version',
    'publisher',
    'engines',
    'categories',
    'activationEvents',
    'main',
    'contributes',
];

const REQUIRED_COMMANDS = [
    'watermarkremover.cleanText',
    'watermarkremover.cleanFile',
    'watermarkremover.detectText',
];

const REQUIRED_SETTINGS = [
    'watermarkremover.binaryPath',
    'watermarkremover.preferMcp',
    'watermarkremover.statistical',
    'watermarkremover.showNotifications',
];

const SUPPORTED_VSCODE_RANGE = /^\^1\.(8[5-9]|9[0-9])\.0$/;

test('package.json has all required top-level fields', () => {
    for (const field of REQUIRED_TOP_FIELDS) {
        assert.ok(pkg[field] !== undefined, `package.json is missing required field "${field}"`);
    }
});

test('package.json name is "watermark-remover" and publisher is "techbuzzz"', () => {
    assert.equal(pkg.name, 'watermark-remover');
    assert.equal(pkg.publisher, 'techbuzzz');
});

test('package.json version is a valid SemVer string', () => {
    assert.match(
        pkg.version,
        /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/,
        `version "${pkg.version}" must be SemVer`,
    );
});

test('package.json requires VS Code 1.85 or later', () => {
    assert.ok(pkg.engines.vscode, 'engines.vscode is required');
    assert.match(
        pkg.engines.vscode,
        SUPPORTED_VSCODE_RANGE,
        `engines.vscode "${pkg.engines.vscode}" must match ${SUPPORTED_VSCODE_RANGE}`,
    );
});

test('package.json requires Node 18+', () => {
    assert.ok(pkg.engines.node, 'engines.node is required');
    assert.match(
        pkg.engines.node,
        />=1[89]\.|>=2[0-9]\./,
        `engines.node "${pkg.engines.node}" must be 18+`,
    );
});

test('package.json activationEvents covers the three commands', () => {
    const events = pkg.activationEvents;
    assert.ok(Array.isArray(events), 'activationEvents must be an array');
    for (const command of REQUIRED_COMMANDS) {
        assert.ok(
            events.some((e) => typeof e === 'string' && e === `onCommand:${command}`),
            `activationEvents must include onCommand:${command}`,
        );
    }
});

test('package.json contributes.commands registers the three commands', () => {
    const commands = pkg.contributes?.commands;
    assert.ok(Array.isArray(commands), 'contributes.commands must be an array');
    const ids = commands.map((c) => c.command);
    for (const required of REQUIRED_COMMANDS) {
        assert.ok(ids.includes(required), `contributes.commands must include ${required}`);
    }
    // Every command must have a non-empty title.
    for (const c of commands) {
        assert.ok(typeof c.title === 'string' && c.title.length > 0, `command ${c.command} is missing a title`);
        assert.equal(c.category, 'WatermarkRemover', `command ${c.command} must use the WatermarkRemover category`);
    }
});

test('package.json contributes.menus wires every command to its context', () => {
    const menus = pkg.contributes?.menus;
    assert.ok(menus, 'contributes.menus is required');
    // Text commands should appear in editor/context and the contextual variant.
    for (const menuName of ['editor/context', 'editor/context/contextual']) {
        const menu = menus[menuName];
        assert.ok(Array.isArray(menu), `menus.${menuName} must be an array`);
        const ids = menu.map((m) => m.command);
        assert.ok(ids.includes('watermarkremover.cleanText'), `menus.${menuName} must include cleanText`);
        assert.ok(ids.includes('watermarkremover.detectText'), `menus.${menuName} must include detectText`);
    }
    // File command should appear in explorer/context and the contextual variant.
    for (const menuName of ['explorer/context', 'explorer/context/contextual']) {
        const menu = menus[menuName];
        assert.ok(Array.isArray(menu), `menus.${menuName} must be an array`);
        const ids = menu.map((m) => m.command);
        assert.ok(ids.includes('watermarkremover.cleanFile'), `menus.${menuName} must include cleanFile`);
    }
});

test('package.json contributes.configuration defines all 4 settings', () => {
    const properties = pkg.contributes?.configuration?.properties;
    assert.ok(properties, 'contributes.configuration.properties is required');
    for (const key of REQUIRED_SETTINGS) {
        assert.ok(properties[key], `contributes.configuration.properties.${key} is required`);
    }
});

test('package.json build script uses tsc and prepublish runs build', () => {
    assert.equal(pkg.scripts?.build, 'tsc -p .', 'scripts.build must be "tsc -p ."');
    assert.equal(pkg.scripts?.['vscode:prepublish'], 'npm run build', 'vscode:prepublish must trigger the build');
    assert.match(pkg.scripts?.test ?? '', /^node --test /, 'scripts.test must use node --test');
});

test('package.json devDependencies includes typescript and @types/vscode', () => {
    assert.ok(pkg.devDependencies?.typescript, 'typescript devDependency is required');
    assert.ok(pkg.devDependencies?.['@types/vscode'], '@types/vscode devDependency is required');
    assert.ok(pkg.devDependencies?.['@types/node'], '@types/node devDependency is required');
});

test('extension source file exists and references all three commands', () => {
    const extPath = path.join(__dirname, '..', 'src', 'extension.ts');
    assert.ok(fs.existsSync(extPath), 'src/extension.ts is required');
    const src = fs.readFileSync(extPath, 'utf8');
    for (const command of REQUIRED_COMMANDS) {
        assert.ok(
            src.includes(command),
            `src/extension.ts must reference command ${command}`,
        );
    }
    // The extension must use child_process.spawn to talk to the CLI.
    assert.ok(src.includes("from 'node:child_process'") || src.includes("from 'child_process'"),
        'src/extension.ts must import from node:child_process (or child_process)');
});

test('tsconfig.json is present and targets ES2022 with strict mode', () => {
    const tsconfigPath = path.join(__dirname, '..', 'tsconfig.json');
    assert.ok(fs.existsSync(tsconfigPath), 'tsconfig.json is required');
    const cfg = JSON.parse(fs.readFileSync(tsconfigPath, 'utf8'));
    assert.equal(cfg.compilerOptions?.target, 'ES2022', 'tsconfig target must be ES2022');
    assert.equal(cfg.compilerOptions?.strict, true, 'tsconfig strict must be true');
    assert.ok(Array.isArray(cfg.include) && cfg.include.includes('src/**/*'),
        'tsconfig include must cover src/**/*');
});

test('bundled skills/ folder ships the master + five per-format skills', () => {
    const skillsDir = path.join(__dirname, '..', 'skills');
    assert.ok(fs.existsSync(skillsDir), 'skills/ directory is required');
    const required = [
        'watermark-remover',
        'clean-text',
        'clean-markdown',
        'clean-file',
        'clean-image',
        'detect',
    ];
    for (const name of required) {
        const skillPath = path.join(skillsDir, name, 'SKILL.md');
        assert.ok(fs.existsSync(skillPath), `skills/${name}/SKILL.md is required`);
    }
});

test('master skill frontmatter mentions vscode compatibility', () => {
    const masterSkillPath = path.join(__dirname, '..', 'skills', 'watermark-remover', 'SKILL.md');
    const src = fs.readFileSync(masterSkillPath, 'utf8');
    assert.ok(src.startsWith('---'), 'master skill must start with YAML frontmatter');
    assert.match(src, /^compatibility:\s*.+vscode.+/m,
        'master skill compatibility must list vscode');
});

test('README.md is a marketplace listing with install instructions', () => {
    const readmePath = path.join(__dirname, '..', 'README.md');
    assert.ok(fs.existsSync(readmePath), 'README.md is required');
    const src = fs.readFileSync(readmePath, 'utf8');
    assert.ok(src.length > 1000, 'README.md should be substantial');
    assert.ok(src.includes('Requirements') || src.includes('requirements'),
        'README.md should describe the binary requirement');
    assert.ok(/install|Install/i.test(src), 'README.md should include install instructions');
});
