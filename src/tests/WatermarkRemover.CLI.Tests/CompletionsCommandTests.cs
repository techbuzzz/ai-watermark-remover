using FluentAssertions;
using Spectre.Console.Cli;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.CLI.Infrastructure;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Tests for the <c>completions</c> command and the static script
/// generators that back it. The acceptance gate from the TODO is:
/// "the emitted script contains the command names". These tests
/// also assert that the script is well-formed enough to be piped to
/// the shell without crashing.
/// </summary>
public class CompletionsCommandTests
{
    private static CommandContext NewContext() => new([], new EmptyRemainingArgs(), "completions", null);

    public static IEnumerable<object[]> AllShells() =>
        ShellCompletionScripts.SupportedShells.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(AllShells))]
    public void Render_ForEachSupportedShell_ContainsEveryCommandName(string shell)
    {
        string script = ShellCompletionScripts.Render(shell);

        // Acceptance: the script names the main sub-commands so the
        // user can see them on <Tab>.
        script.Should().Contain("clean-text");
        script.Should().Contain("clean-markdown");
        script.Should().Contain("clean-image");
        script.Should().Contain("serve");
    }

    [Theory]
    [MemberData(nameof(AllShells))]
    public void Render_ForEachSupportedShell_IsNonEmpty(string shell)
    {
        string script = ShellCompletionScripts.Render(shell);
        script.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Render_Bash_RegistersCompleteForWatermarkremover()
    {
        string script = ShellCompletionScripts.Render("bash");

        script.Should().Contain("complete -F _watermarkremover watermarkremover");
        script.Should().Contain("_watermarkremover()");
        // Common flag surfaced so it tab-completes.
        script.Should().Contain("--json");
        script.Should().Contain("--config");
    }

    [Fact]
    public void Render_Bash_ListsEveryCommand()
    {
        string script = ShellCompletionScripts.Render("bash");

        foreach (string cmd in ShellCompletionScripts.Commands)
        {
            script.Should().Contain(cmd, because: $"bash completion must list the '{cmd}' sub-command");
        }
    }

    [Fact]
    public void Render_Zsh_HasCompdefHeader()
    {
        // Zsh requires the `#compdef watermarkremover` first line for
        // the script to be picked up automatically.
        string script = ShellCompletionScripts.Render("zsh");

        script.Should().StartWith("#compdef watermarkremover");
    }

    [Fact]
    public void Render_PowerShell_UsesRegisterArgumentCompleter()
    {
        string script = ShellCompletionScripts.Render("powershell");

        script.Should().Contain("Register-ArgumentCompleter");
        script.Should().Contain("'watermarkremover'");
        script.Should().Contain("'clean-text'");
    }

    [Fact]
    public void Render_Fish_UsesCompleteBuiltins()
    {
        string script = ShellCompletionScripts.Render("fish");

        script.Should().Contain("complete -c watermarkremover");
        script.Should().Contain("'clean-text'");
    }

    [Fact]
    public void Render_UnknownShell_Throws()
    {
        Action act = () => ShellCompletionScripts.Render("tcsh");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unsupported shell*");
    }

    [Fact]
    public async Task ExecuteAsync_Bash_PrintsScriptToStdout()
    {
        // We swap Console.Out and Console.Error to a single StringWriter so the
        // assertion sees the exact bytes the command emitted. The writer is
        // disposed only after Console.* has been restored, so any subsequent
        // AnsiConsole.* call from another test hits the real std streams.
        CompletionsCommand command = new();
        var settings = new CompletionsCommand.Settings { Shell = "bash" };
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        using StringWriter capture = new();
        try
        {
            Console.SetOut(capture);
            Console.SetError(capture);

            int exit = await command.ExecuteAsync(NewContext(), settings);

            exit.Should().Be(0);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        string rendered = capture.ToString();
        rendered.Should().Contain("clean-text");
        rendered.Should().Contain("complete -F _watermarkremover watermarkremover");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownShell_ReturnsNonZero()
    {
        CompletionsCommand command = new();
        var settings = new CompletionsCommand.Settings { Shell = "elvish" };

        // The error path writes to the Spectre renderer; we don't need to
        // capture it, but we still need the command's exit code to be 1.
        int exit = await command.ExecuteAsync(NewContext(), settings);
        exit.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyShell_ReturnsNonZero()
    {
        CompletionsCommand command = new();
        var settings = new CompletionsCommand.Settings { Shell = "" };

        int exit = await command.ExecuteAsync(NewContext(), settings);
        exit.Should().Be(1);
    }

    /// <summary>Stub implementation of <see cref="IRemainingArguments"/> — the framework type is internal.</summary>
    private sealed class EmptyRemainingArgs : IRemainingArguments
    {
        public ILookup<string, string?> Parsed => Enumerable.Empty<(string, string?)>().ToLookup(p => p.Item1, p => p.Item2);
        public IReadOnlyList<string> Raw => [];
    }
}
