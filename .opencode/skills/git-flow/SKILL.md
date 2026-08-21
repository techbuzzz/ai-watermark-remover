---
name: git-flow
description: >
  Use when managing git workflow: creating branches, writing commit messages,
  preparing PRs, generating changelogs, managing releases, resolving merge conflicts.
  Follows Conventional Commits specification.
license: MIT
compatibility: opencode
---

# Git Flow Skill

## When to Use

Activate this skill when the user:
- Asks to commit changes with a good commit message
- Needs help with branch naming
- Wants to create a PR description
- Needs to generate a changelog
- Is preparing a release

## Conventional Commits Format

```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Types
| Type | When to use |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `style` | Formatting, no logic change |
| `refactor` | Code restructure, no feature/fix |
| `perf` | Performance improvement |
| `test` | Adding/fixing tests |
| `build` | Build system, dependencies |
| `ci` | CI/CD configuration |
| `chore` | Other maintenance |
| `revert` | Reverting a previous commit |

### Examples
```bash
feat(auth): add OAuth2 login with Google
fix(api): handle null user in GetUserById endpoint
refactor(orders): extract order total calculation to separate method
perf(db): add index on users.email column
docs(readme): update installation instructions
test(orders): add integration tests for CreateOrder endpoint
chore(deps): upgrade Entity Framework Core to 9.0
```

## Branch Naming

```bash
# Feature branches
feat/user-authentication
feat/order-total-calculation

# Bug fix branches
fix/null-reference-in-get-user
fix/order-total-rounding-error

# Release branches
release/v2.1.0
release/v2.1.0-rc.1

# Hotfix branches
hotfix/critical-payment-bug
```

## PR Description Template

```markdown
## Summary
[One paragraph explaining WHAT changed and WHY]

## Changes
- [Change 1]
- [Change 2]

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests pass
- [ ] Manual testing done

## Breaking Changes
[None / Describe breaking changes]

## Related Issues
Closes #123
```

## Changelog Format (Keep a Changelog)

```markdown
## [2.1.0] - 2025-07-17

### Added
- OAuth2 login with Google (#123)
- Export to CSV in user dashboard (#145)

### Fixed
- Null reference exception in GetUserById (#167)
- Order total rounding error (#156)

### Changed
- Upgraded to .NET 9 (#170)

### Deprecated
- Old REST endpoint `/api/v1/users` (use `/api/v2/users`)
```

## Checklist

Before committing:
- [ ] Commit message follows Conventional Commits
- [ ] Scope is correct and consistent with repo conventions
- [ ] No sensitive data (keys, passwords) in commit
- [ ] Related issue/PR number in footer if applicable
- [ ] Breaking changes marked with `!` or `BREAKING CHANGE:` footer
