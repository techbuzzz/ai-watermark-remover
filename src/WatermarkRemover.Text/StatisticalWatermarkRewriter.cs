using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.Text;

/// <summary>
/// Layer B — best-effort disruption of statistical (Kirchenbauer-style green-list)
/// watermarks. Detects likely green-list tokens using a built-in AI-vocabulary dictionary,
/// swaps them for semantic equivalents, optionally performs LLM back-translation via an
/// OpenAI-compatible endpoint, and applies light heuristic paraphrasing.
/// </summary>
public sealed partial class StatisticalWatermarkRewriter(HttpClient? httpClient = null) : IStatisticalWatermarkRewriter
{
    private readonly HttpClient? _httpClient = httpClient;

    // Unicode-aware: matches Latin, Cyrillic (Russian) and other scripts.
    [GeneratedRegex(@"[\p{L}][\p{L}'\-]*")]
    private static partial Regex WordRegex();

    /// <inheritdoc />
    public async Task<TextCleanResult> RewriteAsync(string input, TextCleanOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        if (input.Length == 0)
        {
            return TextCleanResult.Empty;
        }

        var removed = new List<RemovedItem>();

        // 1. Green-list token detection + synonym substitution.
        string rewritten = SwapGreenListTokens(input, removed);

        // 2. Optional LLM back-translation.
        if (!string.IsNullOrWhiteSpace(options.LlmEndpoint) && _httpClient is not null)
        {
            string? translated = await TryBackTranslateAsync(rewritten, options, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                removed.Add(new RemovedItem("llm-back-translation", 0, 0, $"Paraphrased via LLM endpoint '{options.LlmEndpoint}'"));
                rewritten = translated;
            }
        }

        // 3. Light heuristic paraphrase (optional).
        if (options.EnableHeuristicParaphrase)
        {
            rewritten = HeuristicParaphrase(rewritten, removed);
        }

        int totalWords = WordRegex().Matches(input).Count;
        double confidence = totalWords == 0 ? 0.0 : Math.Min(1.0, (double)removed.Count / totalWords * 3.0);
        return new TextCleanResult(input, rewritten, removed, [], confidence);
    }

    /// <summary>
    /// Treats tokens present in the AI-vocabulary dictionary as candidate green-list tokens and
    /// replaces them with a semantically equivalent alternative chosen deterministically.
    /// </summary>
    private static string SwapGreenListTokens(string input, List<RemovedItem> removed)
    {
        return WordRegex().Replace(input, match =>
        {
            string word = match.Value;
            if (!SynonymDictionary.Map.TryGetValue(word, out string[]? options) || options.Length == 0)
            {
                return word;
            }

            // Deterministic pick so results are reproducible.
            int idx = (int)(unchecked((uint)StableHash(word, match.Index)) % (uint)options.Length);
            string replacement = MatchCase(word, options[idx]);
            removed.Add(new RemovedItem("statistical-rewrite", match.Index, word.Length, $"Green-list token '{word}' → '{replacement}'"));
            return replacement;
        });
    }

    /// <summary>Very light reordering: normalizes doubled spaces introduced by substitution.</summary>
    private static string HeuristicParaphrase(string input, List<RemovedItem> removed)
    {
        string collapsed = MultiSpaceRegex().Replace(input, " ");
        if (collapsed != input)
        {
            removed.Add(new RemovedItem("heuristic-paraphrase", 0, 0, "Collapsed whitespace after token substitution"));
        }

        return collapsed;
    }

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    private async Task<string?> TryBackTranslateAsync(string text, TextCleanOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // OpenAI-compatible chat completions endpoint.
            string baseUrl = options.LlmEndpoint!.TrimEnd('/');
            string url = $"{baseUrl}/v1/chat/completions";
            var payload = new
            {
                model = options.LlmModel ?? "llama3",
                messages = new object[]
                {
                    new { role = "system", content = "You paraphrase text to remove statistical watermarks while preserving meaning and formatting. Reply with only the paraphrased text." },
                    new { role = "user", content = text },
                },
                temperature = 0.7,
                stream = false,
            };

            using var response = await _httpClient!.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken).ConfigureAwait(false);
            return completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            // Best-effort: swallow network/parse errors and fall back to the rewritten text.
            return null;
        }
    }

    private static string MatchCase(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0)
        {
            return replacement;
        }

        if (char.IsUpper(original[0]) && original.All(c => char.IsUpper(c) || !char.IsLetter(c)) && original.Any(char.IsUpper) && original.Length > 1)
        {
            return replacement.ToUpperInvariant();
        }

        if (char.IsUpper(original[0]))
        {
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }

        return replacement;
    }

    private static int StableHash(string word, int position)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in word)
            {
                hash = (hash * 31) + char.ToLowerInvariant(c);
            }

            hash = (hash * 31) + position;
            return hash;
        }
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }

        public sealed class Choice
        {
            public ChoiceMessage? Message { get; set; }
        }

        public sealed class ChoiceMessage
        {
            public string? Content { get; set; }
        }
    }
}
