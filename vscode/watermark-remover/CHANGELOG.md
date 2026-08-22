# Changelog — WatermarkRemover VS Code extension

All notable changes to the **WatermarkRemover** VS Code extension are
documented in this file. The format is based on [Keep a
Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial release. The extension ships:
  - `watermarkremover.cleanText` command — right-click a text
    selection → "Clean AI watermarks". Spawns
    `watermarkremover clean-text --stdin` and replaces the selection
    with the cleaned text.
  - `watermarkremover.cleanFile` command — right-click a file in the
    Explorer → "Strip metadata". Spawns
    `watermarkremover clean-file` and writes a
    `<name>-clean<ext>` sibling.
  - `watermarkremover.detectText` command — select text → "Detect AI
    watermarks". Opens the JSON result in a new editor tab.
  - `editor/context` and `editor/context/contextual` menus for the
    text commands, `explorer/context` and
    `explorer/context/contextual` menus for the file command.
  - Four configuration settings under
    `watermarkremover.*`:
    `binaryPath`, `preferMcp` (reserved), `statistical`,
    `showNotifications`.
  - Bundled `skills/` folder (master `watermark-remover` + five
    per-format skills) so any AI agent running inside VS Code
    (Continue, Cline, …) can learn the tool.

### Notes

- The extension is **dependency-free at runtime** — it uses only
  Node built-ins (`child_process`, `node:fs`) and the `vscode`
  module. The compiled output is a single
  `out/extension.js` file (≈ 16 KB).
- The `watermarkremover` CLI must be installed separately
  (download from GitHub Releases or build from source). The
  extension surfaces a clear install-instructions message when
  the binary is missing.
