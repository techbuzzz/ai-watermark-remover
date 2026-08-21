# 🐚 Shell completion

`watermarkremover` ships a built-in shell-completion generator. The
`completions` command emits a static script for one of the four
supported shells: **bash**, **zsh**, **PowerShell**, or **fish**.

```bash
watermarkremover completions --shell <bash|zsh|powershell|fish>
```

The script lists every top-level sub-command and a curated set of the
most common flags per command. The full flag set is always available via
`watermarkremover <cmd> --help`.

## Bash

```bash
# System-wide (Linux, requires sudo)
watermarkremover completions --shell bash | sudo tee /etc/bash_completion.d/watermarkremover > /dev/null

# User-only
watermarkremover completions --shell bash >> ~/.bashrc
```

Open a new shell (or `source ~/.bashrc`) and `watermarkremover <Tab>`
should list every sub-command.

## Zsh

```bash
# Homebrew on macOS — recommended location
watermarkremover completions --shell zsh | sudo tee "$(brew --prefix)/share/zsh/site-functions/_watermarkremover" > /dev/null

# User-only
watermarkremover completions --shell zsh >> ~/.zshrc
```

Restart the shell or `autoload -U compinit && compinit` once. Zsh
completion requires a `#compdef` header, which the generator emits on
the first line.

## PowerShell

```powershell
watermarkremover completions --shell powershell | Out-File -Append $PROFILE.CurrentUserAllHosts
```

Then open a new PowerShell window. The `Register-ArgumentCompleter`
block is namespaced to the `watermarkremover` command, so it won't
interfere with anything else.

## Fish

```bash
mkdir -p ~/.config/fish/completions
watermarkremover completions --shell fish > ~/.config/fish/completions/watermarkremover.fish
```

Completion is available immediately in any new fish shell.

## What the script does

- **Top-level sub-command completion** — the typed prefix is matched
  against the list of commands (`clean-text`, `clean-markdown`,
  `clean-image`, `serve`, …).
- **Per-command flag completion** — once a sub-command is selected,
  the next token is matched against a curated list of common flags
  for that sub-command plus the global options (`--json`, `--verbose`,
  `--dry-run`, `--output`, `--config`).
- **Stable across option additions** — the generator is static and
  the command list is curated, so the script never references a flag
  the binary doesn't have. Adding a flag to the CLI does **not**
  require regenerating the script.

## Verifying

```bash
# Should print a non-empty script containing every command name.
watermarkremover completions --shell bash | grep -c 'clean-text\|clean-markdown\|clean-image\|serve'

# Should exit 0.
watermarkremover completions --shell bash > /dev/null && echo OK
```

## Troubleshooting

- **Completion not working in bash**: ensure `bash-completion` is
  installed (`brew install bash-completion` on macOS,
  `apt install bash-completion` on Debian/Ubuntu) and sourced from
  your `.bashrc`. Without it, `complete -F` is still defined but
  the helpers (`_init_completion`, `compgen`) won't be loaded.
- **`compdef` not found in zsh**: zsh's completion system is
  disabled by default in some `~/.zshrc` setups. Make sure
  `autoload -U compinit && compinit` runs before sourcing the
  completion script.
- **PowerShell asks about unapproved verbs**: `Register-ArgumentCompleter`
  is a standard cmdlet; the generated script is safe to run.
