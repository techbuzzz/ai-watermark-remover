<!--
  Thanks for taking the time to contribute! Please fill in this template so we
  can review the change quickly. If the PR is not yet ready for review, mark
  it as a Draft (Convert to draft in the Reviewers panel).
-->

## Summary

<!-- One or two sentences. What does this PR change and why? -->

> Example: *Add a TIFF metadata cleaner that strips EXIF / XMP / IPTC / ICC
> from `.tif` and `.tiff` files, registered in `FileCleanerRouter` and
> covered by 5 round-trip tests.*

## Related issue

<!-- If this PR closes or relates to an existing issue, link it here. -->
<!-- Replace with `Closes #N` (or `Fixes #N` / `Refs #N`) so the bot can
     auto-link and auto-close. -->

- Closes #
- Refs #

## Type of change

<!-- Put an `x` in the box that applies. -->

- [ ] 🐛 Bug fix (non-breaking change that fixes an issue)
- [ ] ✨ New feature (non-breaking change that adds functionality)
- [ ] 💥 Breaking change (fix or feature that would cause existing functionality to change)
- [ ] ⚡ Performance improvement
- [ ] 🧹 Refactor / cleanup
- [ ] 📚 Documentation only
- [ ] 🔧 Build / CI / tooling

## Checklist

<!-- Put an `x` in each box that applies. The first three are required. -->

- [ ] `dotnet build` runs clean (0 warnings, 0 errors). Warnings-as-errors is on.
- [ ] `dotnet test` is green — 62/62 tests pass.
- [ ] New behaviour is covered by unit tests (`[Fact]` / `[Theory]` + `[InlineData]`).
- [ ] I added or updated **public** XML doc comments.
- [ ] I updated [README.md](../README.md) and/or [docs/](../docs/) for any user-facing change.
- [ ] I followed the [commit message convention](../CONTRIBUTING.md#commit-messages) (Conventional Commits).
- [ ] I ran `dotnet format` and there are no remaining changes.
- [ ] I read [CONTRIBUTING.md](../CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md).

## Screenshots / output

<!-- Optional. Paste terminal output, before/after diffs, or screenshots. -->

```
$ dotnet run --project src/WatermarkRemover.CLI -- clean-file photo.tif -o clean.tif
✔ Cleaned photo.tif → clean.tif (3.2 KB metadata removed)
```

## Notes for reviewers

<!-- Anything the reviewer should pay special attention to: design trade-offs,
     areas you weren't sure about, follow-up work that should be filed as a
     separate issue. -->
