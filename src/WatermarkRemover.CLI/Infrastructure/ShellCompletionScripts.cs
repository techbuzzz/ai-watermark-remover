using System.Text;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>
/// Emits shell completion scripts for <c>watermarkremover</c> commands. The
/// scripts are static — they list the sub-commands and a curated set of
/// common flags per command. The single source of truth for the command
/// list lives in <see cref="Commands"/>, so the bash / zsh / PowerShell /
/// fish generators stay in lockstep.
/// </summary>
/// <remarks>
/// Per-command option lists are intentionally short and best-effort:
/// a user who hits &lt;Tab&gt; gets the common flags, and
/// <c>watermarkremover &lt;cmd&gt; --help</c> is the source of truth for
/// the full set. That keeps the generated scripts small and stable across
/// option additions.
/// </remarks>
public static class ShellCompletionScripts
{
    /// <summary>All top-level commands — keep in sync with <c>Program.cs</c>.</summary>
    public static readonly IReadOnlyList<string> Commands =
    [
        "clean-text",
        "clean-markdown",
        "clean-file",
        "clean-image",
        "clean-all",
        "detect-text",
        "detect-markdown",
        "detect-watermark",
        "inspect-file",
        "download-model",
        "serve",
        "serve-mcp",
        "completions",
    ];

    /// <summary>Common global options that every command accepts.</summary>
    private const string GlobalOptions =
        "--json -v --verbose --dry-run -o --output -c --config";

    /// <summary>Subcommand-specific options (best-effort — see remarks).</summary>
    private static readonly IReadOnlyDictionary<string, string> CommandOptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clean-text"] = "-i --input --statistical --unicode --no-vendor --llm-endpoint --llm-model",
            ["clean-markdown"] = "-i --input --strip-all --keep-code",
            ["clean-file"] = "-i --input -r --recursive --preserve-color-profile",
            ["clean-image"] = "-i --input --mask --model --no-auto-mask",
            ["clean-all"] = "-r --recursive --suffix --dry-run",
            ["detect-text"] = "-i --input --vendor",
            ["detect-markdown"] = "-i --input",
            ["detect-watermark"] = "-i --input",
            ["inspect-file"] = "-i --input",
            ["download-model"] = "-d --dest --force",
            ["serve"] = "--port --host --api-key --cors-origins --rate-limit --rate-window --max-upload-mb --no-ui",
            ["serve-mcp"] = "--transport --host --port --api-key --rate-limit --rate-window",
            ["completions"] = "--shell",
        };

    /// <summary>Render the completion script for the requested shell.</summary>
    public static string Render(string shell)
    {
        string normalized = shell.Trim().ToLowerInvariant();
        return normalized switch
        {
            "bash" => RenderBash(),
            "zsh" => RenderZsh(),
            "powershell" or "pwsh" => RenderPowerShell(),
            "fish" => RenderFish(),
            _ => throw new ArgumentException(
                $"Unsupported shell: '{shell}'. Supported: bash, zsh, powershell, fish.",
                nameof(shell)),
        };
    }

    /// <summary>Returns the supported shell names (for help + validation).</summary>
    public static IReadOnlyList<string> SupportedShells { get; } = ["bash", "zsh", "powershell", "fish"];

    private static string RenderBash()
    {
        StringBuilder sb = new();
        sb.AppendLine("# bash completion for watermarkremover");
        sb.AppendLine("# Install: watermarkremover completions --shell bash | sudo tee /etc/bash_completion.d/watermarkremover > /dev/null");
        sb.AppendLine("# Or:      watermarkremover completions --shell bash >> ~/.bashrc");
        sb.AppendLine();
        sb.AppendLine($"_watermarkremover() {{");
        sb.AppendLine("    local cur prev cword commands");
        sb.AppendLine("    cword=${COMP_CWORD:-0}");
        sb.AppendLine("    cur=${COMP_WORDS[cword]}");
        sb.AppendLine($"    commands=\"{string.Join(' ', Commands)}\"");
        sb.AppendLine();
        sb.AppendLine("    if [[ $cword -eq 1 ]]; then");
        sb.AppendLine("        COMPREPLY=( $(compgen -W \"$commands\" -- \"$cur\") )");
        sb.AppendLine("        return 0");
        sb.AppendLine("    fi");
        sb.AppendLine();
        sb.AppendLine("    local options");
        sb.AppendLine("    case \"${COMP_WORDS[1]}\" in");
        foreach (KeyValuePair<string, string> kv in CommandOptions)
        {
            sb.AppendLine($"        {kv.Key}) options=\"{GlobalOptions} {kv.Value}\" ;;");
        }
        sb.AppendLine("        *) options=\"\" ;;");
        sb.AppendLine("    esac");
        sb.AppendLine();
        sb.AppendLine("    if [[ -n $options ]]; then");
        sb.AppendLine("        COMPREPLY=( $(compgen -W \"$options\" -- \"$cur\") )");
        sb.AppendLine("    fi");
        sb.AppendLine("}");
        sb.AppendLine("complete -F _watermarkremover watermarkremover");
        return sb.ToString();
    }

    private static string RenderZsh()
    {
        // Zsh supports bash completion via `bashcompinit`, but a real
        // `#compdef` block is more idiomatic and gives nicer formatting.
        StringBuilder sb = new();
        sb.AppendLine("#compdef watermarkremover");
        sb.AppendLine("# zsh completion for watermarkremover");
        sb.AppendLine("# Install: watermarkremover completions --shell zsh | sudo tee \"$(brew --prefix)/share/zsh/site-functions/_watermarkremover\" > /dev/null");
        sb.AppendLine("# Or:      watermarkremover completions --shell zsh >> ~/.zshrc");
        sb.AppendLine();
        sb.AppendLine("_watermarkremover() {");
        sb.AppendLine("    local -a commands");
        sb.AppendLine($"    commands=(");
        foreach (string cmd in Commands)
        {
            sb.AppendLine($"        '{cmd}:{Describe(cmd)}'");
        }
        sb.AppendLine("    )");
        sb.AppendLine();
        sb.AppendLine("    _describe 'command' commands");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("compdef _watermarkremover watermarkremover");
        return sb.ToString();
    }

    private static string RenderPowerShell()
    {
        StringBuilder sb = new();
        sb.AppendLine("# PowerShell completion for watermarkremover");
        sb.AppendLine("# Install: watermarkremover completions --shell powershell >> $PROFILE.CurrentUserAllHosts");
        sb.AppendLine();
        sb.AppendLine("Register-ArgumentCompleter -Native -CommandName 'watermarkremover' -ScriptBlock {");
        sb.AppendLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        sb.AppendLine();
        sb.AppendLine("    $commands = @(");
        foreach (string cmd in Commands)
        {
            sb.AppendLine($"        '{cmd}',");
        }
        sb.AppendLine("    )");
        sb.AppendLine();
        sb.AppendLine("    # Top-level command name completion.");
        sb.AppendLine("    $commandElements = $commandAst.CommandElements");
        sb.AppendLine("    if ($commandElements.Count -lt 3) {");
        sb.AppendLine("        $commands | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object {");
        sb.AppendLine("            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)");
        sb.AppendLine("        }");
        sb.AppendLine("        return");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    # Per-command option completion (best-effort; see `watermarkremover <cmd> --help`).");
        sb.AppendLine("    $subcommand = $commandElements[1].Extent.Text");
        sb.AppendLine($"    $global = '{GlobalOptions}'");
        sb.AppendLine("    $options = @{");
        foreach (KeyValuePair<string, string> kv in CommandOptions)
        {
            sb.AppendLine($"        '{kv.Key}' = '{kv.Value}'");
        }
        sb.AppendLine("    }");
        sb.AppendLine("    $extra = if ($options.ContainsKey($subcommand)) { $options[$subcommand] } else { '' }");
        sb.AppendLine("    $all = ($global + ' ' + $extra) -split ' ' | Where-Object { $_ -ne '' }");
        sb.AppendLine("    $all | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object {");
        sb.AppendLine("        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string RenderFish()
    {
        StringBuilder sb = new();
        sb.AppendLine("# fish completion for watermarkremover");
        sb.AppendLine("# Install: watermarkremover completions --shell fish > ~/.config/fish/completions/watermarkremover.fish");
        sb.AppendLine();
        sb.AppendLine("# Top-level sub-commands");
        foreach (string cmd in Commands)
        {
            sb.AppendLine($"complete -c watermarkremover -n '__fish_use_subcommand' -a '{cmd}' -d '{Describe(cmd)}'");
        }
        sb.AppendLine();
        sb.AppendLine("# Global options available on every command");
        foreach (string opt in GlobalOptions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.AppendLine($"complete -c watermarkremover -l '{opt.TrimStart('-')}'");
        }
        sb.AppendLine();
        sb.AppendLine("# Per-command options (best-effort; see `watermarkremover <cmd> --help`).");
        foreach (KeyValuePair<string, string> kv in CommandOptions)
        {
            foreach (string opt in kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (opt.StartsWith("--", StringComparison.Ordinal))
                {
                    sb.AppendLine($"complete -c watermarkremover -n '__fish_seen_subcommand_from {kv.Key}' -l '{opt.TrimStart('-')}'");
                }
                else if (opt.StartsWith('-') && !opt.StartsWith("--", StringComparison.Ordinal))
                {
                    string shortForm = opt.TrimStart('-').TrimEnd('|', '-');
                    sb.AppendLine($"complete -c watermarkremover -n '__fish_seen_subcommand_from {kv.Key}' -s '{shortForm}'");
                }
            }
        }
        return sb.ToString();
    }

    private static string Describe(string command) => command switch
    {
        "clean-text" => "Clean plain text (Layers A/B/C).",
        "clean-markdown" => "Clean markdown, preserving code blocks.",
        "clean-file" => "Strip metadata from files (batch capable).",
        "clean-image" => "Remove visual watermarks via inpainting.",
        "clean-all" => "Auto-route a path to the right pipeline.",
        "detect-text" => "Detect watermark signatures in text.",
        "detect-markdown" => "Detect AI artifacts in markdown.",
        "detect-watermark" => "Detect visual watermark regions in an image.",
        "inspect-file" => "Report metadata found in a file.",
        "download-model" => "Download the LaMa ONNX inpainting model.",
        "serve" => "Host the HTTP API.",
        "completions" => "Emit a shell completion script.",
        _ => command,
    };
}
