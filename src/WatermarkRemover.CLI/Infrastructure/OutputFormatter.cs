using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>Renders command results either as JSON (machine readable) or as rich Spectre.Console output.</summary>
public static class OutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialize <paramref name="value"/> to indented JSON.</summary>
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>Write JSON to stdout.</summary>
    public static void WriteJson<T>(T value) => Console.Out.WriteLine(ToJson(value));

    /// <summary>Render a labelled success panel.</summary>
    public static void Success(string message) =>
        AnsiConsole.MarkupLine($"[green]\u2714[/] {Markup.Escape(message)}");

    /// <summary>Render a labelled warning line.</summary>
    public static void Warning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]\u26a0[/] {Markup.Escape(message)}");

    /// <summary>Render a labelled error line to stderr.</summary>
    public static void Error(string message) =>
        AnsiConsole.MarkupLineInterpolated($"[red]\u2718 {message}[/]");

    /// <summary>Render a two-column key/value table.</summary>
    public static void KeyValueTable(string title, IEnumerable<(string Key, string Value)> rows)
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .AddColumn("Key")
            .AddColumn("Value");

        foreach ((string key, string value) in rows)
        {
            table.AddRow(Markup.Escape(key), Markup.Escape(value));
        }

        AnsiConsole.Write(table);
    }
}
