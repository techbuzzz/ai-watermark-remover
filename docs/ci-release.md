# Cutting a Release

The `release` GitHub Actions workflow (`.github/workflows/release.yml`) builds
and publishes self-contained single-file binaries for the four supported
runtime identifiers and attaches them to a GitHub Release.

## Trigger

The workflow runs automatically when a tag matching `v*` is pushed:

```bash
git tag v1.0.0
git push origin v1.0.0
```

It can also be triggered manually via the Actions tab (`workflow_dispatch`).
When run manually you can optionally pass a tag name (without the leading
`v`); leave it empty to re-use the tag of the current commit / `GITHUB_REF`.

## What it produces

For every release the workflow uploads one archive per RID:

| Archive                         | RID         | Runner         |
|---------------------------------|-------------|----------------|
| `watermarkremover-linux-x64`    | `linux-x64` | `ubuntu-latest` |
| `watermarkremover-linux-arm64`  | `linux-arm64` | `ubuntu-latest` |
| `watermarkremover-win-x64`      | `win-x64`   | `windows-latest` |
| `watermarkremover-osx-x64`      | `osx-x64`   | `macos-latest` |

Each archive contains the full `dotnet publish` output of the
`WatermarkRemover.CLI` project:

- `watermarkremover` (or `watermarkremover.exe` on Windows) — self-contained
  single-file executable with the .NET runtime and ASP.NET Core bundled
  inside. Runs on a machine without a pre-installed .NET SDK.
- No PDBs (debug symbols are embedded in the binary via
  `DebugType=embedded`).

## Publish properties

The workflow pins the same flags the `dotnet publish` publish profile is
expected to use:

- `PublishSingleFile=true` — single executable per archive.
- `SelfContained=true` — .NET runtime bundled (no host install required).
- `EnableCompressionInSingleFile=true` — smaller binary.
- `IncludeNativeLibrariesForSelfExtract=true` — required so the
  ASP.NET Core native bits are extracted on first run.
- `DebugType=embedded` — keep the PDB inside the single file so users
  can still get a useful stack trace without us shipping a separate
  `.pdb` per RID.
- `TreatWarningsAsErrors=true` — parity with `build-and-test.yml`.

## Re-running for an existing tag

If a release was cut and the binaries need to be rebuilt (e.g. a CVE in
the bundled runtime), trigger the workflow from the Actions tab and pass
the existing tag (without the leading `v`) into the `tag` input. The
`release` job will detect that the release already exists and call
`gh release upload --clobber` to refresh the assets.

## Permissions and secrets

- `GITHUB_TOKEN` is the only secret used; it requires `contents: write`
  (granted at the top of the workflow). No additional secrets are
  required because every binary is self-contained.
- The workflow does not push tags or amend commits. It only creates the
  GitHub Release and attaches the four archives.

## Versioning

Tags follow strict semver (`vMAJOR.MINOR.PATCH`). Pre-release tags such
as `v1.0.0-rc.1` are matched by the `v*` pattern and work out of the
box; the GitHub UI will mark them as pre-releases automatically because
of the `-` in the tag name.
