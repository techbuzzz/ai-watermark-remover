# Contributing to WatermarkRemover

Thanks for your interest in making WatermarkRemover better! 🎉
This document explains how to set up your environment, propose changes, and get them
merged quickly.

> **First time contributing to an open-source project?** Look for issues tagged
> [`good first issue`](https://github.com/techbuzzz/ai-watermark-remover/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
> — we curate a few every release to make onboarding painless.

---

## 📋 Table of contents

- [Code of Conduct](#code-of-conduct)
- [Quick checklist](#quick-checklist)
- [Development environment](#development-environment)
- [Project layout](#project-layout)
- [Coding conventions](#coding-conventions)
- [Testing](#testing)
- [Commit messages](#commit-messages)
- [Pull request process](#pull-request-process)
- [Adding a new vendor detector](#adding-a-new-vendor-detector)
- [Adding a new metadata cleaner](#adding-a-new-metadata-cleaner)
- [Documentation](#documentation)
- [Release process](#release-process)
- [Getting help](#getting-help)

---

## 🤝 Code of Conduct

This project follows the [Contributor Covenant](./CODE_OF_CONDUCT.md). By participating,
you agree to uphold it. Unacceptable behaviour can be reported to
[conduct@techbuzzz.dev](mailto:conduct@techbuzzz.dev) (replace with the maintainer's
contact if different).

---

## ✅ Quick checklist

A pull request that touches code should:

- [ ] Build locally with `dotnet build` and **0 warnings, 0 errors** (warnings-as-errors is on).
- [ ] Pass `dotnet test` with **62/62 tests green**.
- [ ] Add or update unit tests for the new behavior.
- [ ] Update [README.md](./README.md), [docs/](./docs/), or inline XML doc comments as needed.
- [ ] Follow the [commit message convention](#commit-messages).
- [ ] Pass `dotnet format` (run from the repo root, no changes should remain).
- [ ] Be ≤ ~400 lines of diff (split larger changes into stacked PRs).

---

## 🛠️ Development environment

### Prerequisites

| Tool                | Version  | Why                                    |
|---------------------|----------|----------------------------------------|
| .NET SDK            | **10.0** | Pinned via [`global.json`](./global.json) (10.0.400) |
| Git                 | 2.40+    | For commit signing + submodules        |
| (Optional) Docker   | 24+      | To build the container image           |
| (Optional) Ollama   | 0.3+     | To exercise Layer B LLM back-translation |

> The repo pins the SDK with `global.json` (`rollForward: latestPatch`,
> `allowPrerelease: false`). If you have a different SDK installed, install .NET 10.0.400
> or set `DOTNET_ROLL_FORWARD_TO_PRERELEASE=0`.

### First-time setup

```bash
git clone https://github.com/techbuzzz/ai-watermark-remover.git
cd ai-watermark-remover
dotnet workload restore   # no-op for now; reserved for future ML workloads
dotnet build
dotnet test               # should print "Total: 62, Failed: 0"
```

You can now run the CLI with `dotnet run --project src/WatermarkRemover.CLI -- <command>`.

### Editor setup

- **VS Code** — install the C# Dev Kit extension. `omnisharp.json` is not needed;
  the repo's `.editorconfig` is the source of truth.
- **Rider** — opens the solution out of the box; respect `.editorconfig`.
- **Visual Studio 2022 17.12+** — same; respect `.editorconfig`.

---

## 🗂️ Project layout

```
WatermarkRemover.sln
├── src/
│   ├── WatermarkRemover.Core        # Models, interfaces, DI contracts (no behaviour)
│   ├── WatermarkRemover.Text        # Text + markdown cleaning (Layer A/B/C)
│   ├── WatermarkRemover.Metadata    # JPEG/PNG/PDF/DOCX/HTML cleaners
│   ├── WatermarkRemover.Image       # Mask generation + LaMa inpainting
│   └── WatermarkRemover.CLI         # Spectre.Console CLI + ASP.NET Core HTTP API
└── src/tests/
    ├── WatermarkRemover.Text.Tests
    ├── WatermarkRemover.Metadata.Tests
    └── WatermarkRemover.Image.Tests
```

- `Core` is the **only** project other modules may depend on transitively.
- `Text`, `Metadata`, `Image` must not reference each other.
- `CLI` is the composition root; everything wires up in
  [`Program.cs`](./src/WatermarkRemover.CLI/Program.cs).

---

## 🎨 Coding conventions

- C# **latest** language version (`.editorconfig` → `csharp_lang_version = latest`).
- **Nullable reference types** are enabled everywhere.
- **Warnings-as-errors** is on for all `src/` projects (see
  [`Directory.Build.props`](./Directory.Build.props)). Don't suppress warnings
  locally — fix the root cause.
- `dotnet format` is run in CI; do it locally before pushing.
- Prefer **expression-bodied members** and **file-scoped namespaces** (project default).
- Use **`record` / `record struct`** for value-like DTOs and config options.
- Use **`Microsoft.Extensions.Logging`** — never `Console.WriteLine` in library code.
  CLI-side formatting goes through `OutputFormatter` in the CLI project.
- No `async void`. No `.Result` / `.Wait()`. No `Thread.Sleep` in async paths.
- **No new top-level NuGet references** without first discussing in an issue
  (the project will move to `Directory.Packages.props` soon — see [BACKLOG.md](./BACKLOG.md)).

### Naming

| Element               | Convention                          | Example                        |
|-----------------------|-------------------------------------|--------------------------------|
| Types / methods       | PascalCase                          | `TextCleaningPipeline`         |
| Public properties     | PascalCase                          | `EnableStatistical`            |
| Locals / parameters   | camelCase                           | `cleanedText`                  |
| Private fields        | `_camelCase` (underscore prefix)    | `_textPipeline`                |
| Constants             | PascalCase                          | `DefaultPort`                  |
| Test methods          | `Method_Scenario_Expectation`       | `Clean_StripsZeroWidthSpace`   |

### Documentation

- Every **public** type and member should have an XML doc comment. `CS1591` is on
  the backlog (see [BACKLOG.md → P3](./BACKLOG.md#p3--quality--reliability-ongoing));
  in the meantime, please add `<summary>` blocks voluntarily for new public APIs.
- Comments explain **why**, not **what** — the code is the *what*.

---

## 🧪 Testing

The repo uses **xUnit** + **FluentAssertions**. Run from the repo root:

```bash
dotnet test                                  # all 62 tests
dotnet test --logger "console;verbosity=detailed"
dotnet test --filter "FullyQualifiedName~UnicodeHygiene"   # one suite
```

### Conventions

- One test class per production class (`Foo` → `FooTests`).
- Use `[Fact]` for single cases, `[Theory] + [InlineData]` for parameterised cases.
- **Do not** depend on filesystem paths outside `Path.GetTempPath()`. Tests must run
  in any order on any machine.
- **Do not** download the LaMa model in tests — image tests inject a
  `FakeInpaintRunner` that fakes inference without ONNX.
- **Coverage** is collected in `cobertura` format and uploaded as a CI artifact
  (codecov integration is on the [BACKLOG.md → P3](./BACKLOG.md#p3--quality--reliability-ongoing) list).

### Property-based tests

For `WatermarkRemover.Text.UnicodeHygieneCleaner`, prefer generating random input with
`FsCheck` (already on the backlog) and asserting invariants:

- After `Clean(s)`, the result has no `Char.GetUnicodeCategory(c) ∈ {Control, Format, Surrogate}` chars.
- For Cyrillic input, the cleaned output equals the input (idempotent).
- `Clean(Clean(s)) == Clean(s)` (idempotent).

---

## 📝 Commit messages

We follow [Conventional Commits](https://www.conventionalcommits.org/) **strictly**
because the [release workflow](./.github/workflows/release.yml) and future
`semantic-release` integration depend on it.

### Format

```
<type>(<scope>): <short summary>            ← imperative, ≤ 72 chars
                                            ← blank line
<body wrapped at 100 cols — explain what &
   why, not how>
                                            ← blank line
<footer>  e.g. Closes #123, BREAKING CHANGE:
```

### Types

| Type         | When to use                                            |
|--------------|--------------------------------------------------------|
| `feat`       | New user-facing feature                                |
| `fix`        | Bug fix                                                |
| `perf`       | Performance improvement (no behaviour change)          |
| `refactor`   | Code change that neither fixes a bug nor adds a feature|
| `docs`       | Documentation only                                     |
| `test`       | Add or fix tests                                       |
| `build`      | Build system, CI, dependencies                         |
| `chore`      | Tooling, formatting, non-functional changes            |
| `ci`         | CI pipeline (separate from `build` when CI is its own concern) |

### Scopes (preferred)

`text`, `metadata`, `image`, `cli`, `http`, `core`, `docs`, `docker`, `release`,
`deps`, `config`.

### Examples

```
feat(text): add DeepSeek vendor detector

Heuristic pattern that catches the trailing punctuation
signatures emitted by DeepSeek-67B / V3. Documents itself as
heuristic in the vendor detector interface.

Closes #142
```

```
fix(metadata): preserve ICC profile when strip_c2pa is false

Previously the JPEG cleaner always rebuilt the segment table
without the ICC chunk, breaking colour-managed browsers.
```

---

## 🔁 Pull request process

1. **Branch** from `main`: `git checkout -b feat/<short-topic>` (or `fix/`, `docs/`,
   `chore/`).
2. **Commit** using the conventions above. `git commit --signoff` is appreciated
   but not required.
3. **Push** and open a PR targeting `main`. Fill in the
   [PR template](./.github/PULL_REQUEST_TEMPLATE.md).
4. **CI must be green** — build + tests on `windows-latest` + `ubuntu-latest`.
5. **At least one approval** from a [CODEOWNER](./.github/CODEOWNERS) is required.
6. **Squash & merge** is preferred; the squash commit message will be edited to
   match the PR title.
7. The release workflow **does not auto-run on merge** — releases are cut manually
   by tagging. See [Release process](#release-process).

### Reviewer expectations

- First response within **3 business days**.
- Reviews focus on correctness, tests, and the public contract; do not bikeshed
  style — `dotnet format` handles that.
- If a PR is **stale for 30 days**, it may be closed. Ping a maintainer to reopen.

---

## ➕ Adding a new vendor detector

1. Create `src/WatermarkRemover.Text/Vendors/<Name>WatermarkDetector.cs`.
2. Implement `IAiTextWatermarkDetector` (see existing detectors for the contract).
3. Register the implementation in
   [`WatermarkRemover.Text.DependencyInjection`](./src/WatermarkRemover.Text/DependencyInjection.cs).
4. Add tests in
   `src/tests/WatermarkRemover.Text.Tests/VendorDetectorTests.cs` (or a new file
   if the surface is large).
5. Document the heuristic limitations in the type's XML doc — **never** claim
   the detector is provably correct (the underlying schemes are not public).

---

## ➕ Adding a new metadata cleaner

1. Create `src/WatermarkRemover.Metadata/<Format>MetadataCleaner.cs`.
2. Implement `IFileMetadataCleaner` (`IsSupported`, `Inspect`, `Clean`).
3. Register the format extension in
   [`FileCleanerRouter`](./src/WatermarkRemover.Metadata/FileCleanerRouter.cs).
4. Add round-trip tests: build a synthetic file in memory, clean it, assert that
   the target metadata keys are gone and the body is preserved.
5. Update the [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) module map.

---

## 📚 Documentation

- User-facing docs live in [README.md](./README.md) + [docs/](./docs/).
- Design notes / RFCs go under [docs/](./docs/) and are linked from
  [BACKLOG.md](./BACKLOG.md) when accepted.
- Public API changes must update the README **in the same PR** — the README is
  a contract, not an afterthought.

---

## 🚀 Release process

We use **manual semver tags** + GitHub Releases:

1. `git checkout main && git pull`
2. Update [`CHANGELOG.md`](./CHANGELOG.md) — move items from *Unreleased* into
   a new versioned section.
3. `git tag -s vX.Y.Z -m "Release vX.Y.Z"`
4. `git push origin vX.Y.Z` — triggers
   [`.github/workflows/release.yml`](./.github/workflows/release.yml).
5. The release job builds self-contained single-file binaries for
   `linux-x64`, `linux-arm64`, `win-x64`, `osx-x64` and attaches them to the
   GitHub Release.

Pre-release tags (`vX.Y.Z-rc.N`) are fine — mark them as pre-releases when
creating the GitHub Release.

---

## 💬 Getting help

- 💡 **Questions / ideas** — start a
  [GitHub Discussion](https://github.com/techbuzzz/ai-watermark-remover/discussions).
- 🐛 **Bugs** — file an issue using the
  [bug report template](./.github/ISSUE_TEMPLATE/bug-report.yml).
- ✨ **Feature requests** — file an issue using the
  [feature request template](./.github/ISSUE_TEMPLATE/feature-request.yml).
- 🔐 **Security issues** — **do not** file a public issue; see
  [SECURITY.md](./SECURITY.md).

Happy hacking! 🚀
