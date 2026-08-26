using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace PPG.GuessAPI;

public sealed class GeminiPatternPredictionService : IPatternPredictionService, IAsyncDisposable
{
    private const string DefaultModel = "gemini-3.5-flash-lite";

    private static readonly Schema PredictionSchema = new()
    {
        Type = Google.GenAI.Types.Type.Object,
        Properties = new Dictionary<string, Schema>
        {
            ["explanation"] = new Schema
            {
                Type = Google.GenAI.Types.Type.String,
                Description = "A concise explanation of the strongest pattern, limited to 250 words."
            },
            ["predictedNumbers"] = new Schema
            {
                Type = Google.GenAI.Types.Type.Array,
                Description = "Exactly three unique next predicted digits, ranked strongest first.",
                Items = new Schema
                {
                    Type = Google.GenAI.Types.Type.Integer,
                    Minimum = 0,
                    Maximum = 9
                },
                MinItems = 3,
                MaxItems = 3
            }
        },
        PropertyOrdering = ["explanation", "predictedNumbers"],
        Required = ["explanation", "predictedNumbers"]
    };

    private const string SystemPrompt = """
        You are an expert Numerical Pattern Recognition Specialist.

        Your task:
        1. Parse the provided historical numerical series in chronological order.
        2. Identify horizontal sum logic, vertical sequence rules, or cut-ank (modular complement shift) patterns.
        3. Forecast the three most likely next digits (0-9) that should follow the final value in the series.
           Return three unique digits ranked from strongest/most likely to weakest/least likely.
        4. If the series contains '*', treat it as the value to predict; otherwise predict the next value after the series.
        5. Briefly explain the identified pattern logic in no more than 250 words.
        6. Return the explanation and the three ranked predicted digits using the required JSON response schema.

        Treat the provided series as the only source of truth. When several continuations are possible, choose the
        strongest pattern-supported digits and explain the uncertainty.
        """;

    private const string DeepSystemPrompt = """
        You are an expert Numerical Pattern Recognition Specialist performing strict walk-forward validation.

        Analyze the supplied digit series in chronological order. Your goal is to discover why each digit follows
        the previous history, test that logic on later known digits, and only then predict after the final digit.

        Required method:
        1. Treat the first transition as an observation baseline. Never pretend it was a test when there was not
           enough earlier evidence to forecast it.
        2. Starting as early as evidence permits, work forward one position at a time. At every position, use only
           the digits before that position to choose an explicit rule and forecast the hidden next digit. Do not use
           the target digit to invent the forecast.
        3. Reveal the actual digit, compare it with the forecast, and mark the check PASS or FAIL. Briefly show the
           calculation, such as +n modulo 10, -n modulo 10, complement/cut-ank, alternating shifts, repeating cycles,
           or another concrete context rule.
        4. Track candidate rules by pass rate, number of checks, and recent consecutive passes. Reject coincidental
           rules that only explain one transition. Prefer the most recent repeatable rule that passed out-of-sample.
        5. After validating through the penultimate-to-final transition, take the final actual digit as the starting
           value and calculate exactly three unique next-digit predictions. Rank them strongest first. The first is
           the rule with the best validated support; the other two are fallback rules with the next-best support.
        6. In the concise explanation, include the important walk-forward forecast -> actual -> PASS/FAIL checks,
           identify the winning rule, show its calculation from the final digit, and state uncertainty honestly.
        7. Return the explanation and exactly three ranked digits using the required JSON response schema.

        Example interpretation: for 6,7,8,2,7, explain the observed 6 -> 7 transition, forecast 8 from the history
        available before 8 and check it against actual 8, then independently forecast/check 2 and 7. Finally apply
        the strongest rule that survived those checks to the last 7. The example illustrates the procedure only;
        do not assume its digits or a +1 rule apply to the user's series.

        Treat the supplied series as the only source of truth. Never label a check PASS unless the forecast exactly
        equals the actual digit.
        """;

    private readonly Client? _client;
    private readonly IMemoryCache _cache;

    public GeminiPatternPredictionService(
        IOptions<GeminiPatternPredictionOptions> options,
        IMemoryCache cache)
    {
        _cache = cache;
        var configuredApiKey = options.Value.ApiKey?.Trim();
        var environmentApiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        var apiKey = !string.IsNullOrWhiteSpace(configuredApiKey)
            ? configuredApiKey
            : environmentApiKey;

        Model = string.IsNullOrWhiteSpace(options.Value.Model)
            ? DefaultModel
            : options.Value.Model.Trim();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client = new Client(apiKey: apiKey);
        }
    }

    public bool IsConfigured => _client is not null;

    public string Model { get; }
        
    public async Task<string> PredictAsync(
        string seriesData,
        PatternPredictionMode mode = PatternPredictionMode.Standard,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesData);

        if (_client is null)
        {
            throw new InvalidOperationException("The Gemini API key is not configured.");
        }

        var normalizedSeries = seriesData.Trim();
        var cacheKey = CreateCacheKey(Model, mode, normalizedSeries);
        if (_cache.TryGetValue(cacheKey, out string? cachedPrediction)
            && !string.IsNullOrWhiteSpace(cachedPrediction))
        {
            return cachedPrediction;
        }

        var response = await _client.Models.GenerateContentAsync(
            model: Model,
            contents: mode == PatternPredictionMode.Deep
                ? $"Perform walk-forward rule discovery and validation on this historical series, then predict the top three digits after its final value, strongest first:\n\n{normalizedSeries}"
                : $"Analyze this historical series and predict the top three next digits after its final value, strongest first:\n\n{normalizedSeries}",
            config: new GenerateContentConfig
            {
                SystemInstruction = new Content
                {
                    Parts = [new Part { Text = mode == PatternPredictionMode.Deep ? DeepSystemPrompt : SystemPrompt }]
                },
                Temperature = 0.1,
                MaxOutputTokens = 2_048,
                ResponseMimeType = "application/json",
                ResponseSchema = PredictionSchema
            },
            cancellationToken: cancellationToken);

        var prediction = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prediction))
        {
            throw new InvalidOperationException("Gemini returned an empty prediction.");
        }

        _cache.Set(cacheKey, prediction, TimeSpan.FromHours(12));
        return prediction;
    }

    private static string CreateCacheKey(
        string model,
        PatternPredictionMode mode,
        string seriesData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{model}\n{mode}\n{seriesData}"));
        return $"GeminiPatternPrediction:Top3:{mode}:{Convert.ToHexString(bytes)}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
