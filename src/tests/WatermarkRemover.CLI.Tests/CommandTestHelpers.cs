using System.Reflection;
using Spectre.Console.Cli;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Test-only helpers for invoking Spectre.Console.Cli command methods that
/// the framework now exposes as <c>protected</c>. Reflection is the
/// simplest way to drive <see cref="AsyncCommand{TSettings}.ExecuteAsync"/>
/// from a test assembly without going through the full <c>CommandApp</c>
/// runner (which would require argv parsing and the full configuration
/// pipeline). The lookup is cached per concrete command type.
/// </summary>
internal static class CommandTestHelpers
{
    /// <summary>
    /// Invokes the protected <c>ExecuteAsync(CommandContext, TSettings, CancellationToken)</c>
    /// method on a Spectre async command via reflection.
    /// </summary>
    public static async Task<int> InvokeExecuteAsync<TSettings>(
        object command,
        CommandContext context,
        TSettings settings,
        CancellationToken cancellationToken)
        where TSettings : CommandSettings
    {
        ArgumentNullException.ThrowIfNull(command);

        MethodInfo? method = command.GetType().GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(CommandContext), typeof(TSettings), typeof(CancellationToken)],
            modifiers: null) ?? throw new MissingMethodException(
                command.GetType().FullName,
                "ExecuteAsync(CommandContext, TSettings, CancellationToken)");

        object? result = method.Invoke(command, [context, settings, cancellationToken])
            ?? throw new InvalidOperationException(
                $"ExecuteAsync on {command.GetType().Name} returned null.");

        return await ((Task<int>)result).ConfigureAwait(false);
    }
}
