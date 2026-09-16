using System.Globalization;
using PPG.GuessAPI.Models;

namespace PPG.GuessAPI;

/// <summary>
/// Deterministic digit forecaster. Model selection is performed only on past
/// observations using expanding-window walk-forward validation.
/// </summary>
public sealed class GenericPredictionService : IGenericPredictionService
{
    private const int MinimumHistory = 20;
    private const int PromptTargetCount = 30;
    private const int PromptHistoryCount = 30;

    private static readonly ModelWeights[] Models =
    [
        new("Balanced", .25, .23, .16, .14, .12, .10),
        new("Sequence", .48, .18, .10, .08, .08, .08),
        new("Recent trend", .20, .20, .10, .30, .12, .08),
        new("Periodic", .20, .16, .36, .10, .10, .08),
        new("Frequency-gap", .18, .16, .10, .12, .24, .20)
    ];

    public GenericPredictionResult Predict(string seriesData, int period, int backtestSize)
    {
        var series = ParseSeries(seriesData);
        if (series.Length < MinimumHistory)
        {
            throw new ArgumentException(
                $"Generic requires at least {MinimumHistory} numeric history values.",
                nameof(seriesData));
        }

        if (period is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be between 1 and 7.");
        }

        // Reserve up to 30 earlier values as context, then generate a separate
        // prompt for every one of the latest 30 known target numbers.
        var targetCount = Math.Min(PromptTargetCount, backtestSize);
        var start = Math.Max(1, series.Length - targetCount);
        var evaluations = Models.Select(model => Backtest(series, period, start, model)).ToArray();
        var selected = evaluations
            .OrderByDescending(item => item.HitRate)
            .ThenByDescending(item => item.MeanReciprocalRank)
            .ThenBy(item => Array.IndexOf(Models, item.Model))
            .First();

        var finalHistory = series.TakeLast(PromptHistoryCount).ToArray();
        var ranking = Rank(finalHistory, period, selected.Model);
        var top = ranking.Take(3).ToArray();
        var scores = ranking.Select((item, index) => new GenericDigitScore
        {
            Number = item.Digit.ToString(CultureInfo.InvariantCulture),
            Score = Math.Round(item.Score * 100, 2),
            Rank = index + 1
        }).ToArray();
        var hitRate = Math.Round(selected.HitRate * 100, 1);
        var prompt = BuildPrompt(finalHistory, period, series.Length - start + 2, false);
        var numberPrompts = selected.Checks
            .Select((check, checkIndex) => new GenericNumberPrompt
            {
                Position = checkIndex + 1,
                ActualNumber = check.ActualDigit.ToString(CultureInfo.InvariantCulture),
                PredictedNumbers = check.PredictedDigits
                    .Select(digit => digit.ToString(CultureInfo.InvariantCulture))
                    .ToArray(),
                IsPass = check.IsHit,
                Prompt = BuildPrompt(
                    GetHistoryWindow(series, check.TargetIndex),
                    period,
                    checkIndex + 1,
                    true)
            })
            .ToArray();
        var checks = selected.Checks
            .Select(check =>
                $"- Step {check.TargetIndex + 1}: forecast **{string.Join(", ", check.PredictedDigits)}** " +
                $"-> actual **{check.ActualDigit}** -> **{(check.IsHit ? "PASS" : "FAIL")}**")
            .ToArray();
        var prediction =
            $"### Generic mathematical ensemble\n" +
            $"Selected **{selected.Model.Name}** by expanding-window walk-forward validation. " +
            $"The top-3 historical hit rate is **{hitRate.ToString("0.0", CultureInfo.InvariantCulture)}%** " +
            $"({selected.Hits}/{selected.Attempts}).\n\n" +
            $"### Walk-forward checks\n" +
            $"{string.Join("\n", checks)}\n\n" +
            $"Signals: variable-order sequence matching, first-order transitions, " +
            $"position-in-{period} periodicity, exponential recency, global frequency, and digit-gap pressure.\n\n" +
            $"Top 3: **{string.Join(", ", top.Select(item => item.Digit))}**. " +
            "The hit rate is measured from history, not a guaranteed future accuracy.";

        return new GenericPredictionResult
        {
            Prompt = prompt,
            NumberPrompts = numberPrompts,
            Prediction = prediction,
            PredictedNumber = top[0].Digit.ToString(CultureInfo.InvariantCulture),
            PredictedNumbers = top.Select(item => item.Digit.ToString(CultureInfo.InvariantCulture)).ToArray(),
            Model = $"Generic / {selected.Model.Name}",
            Scores = scores,
            BacktestAttempts = selected.Attempts,
            BacktestHits = selected.Hits,
            BacktestHitRate = hitRate
        };
    }

    private static string BuildPrompt(
        IReadOnlyList<int> history,
        int period,
        int targetPosition,
        bool isHistoricalCheck) =>
        $"""
        You are a numerical-pattern analyst. This is {(isHistoricalCheck ? $"the prompt for known number {targetPosition}" : "the final next-number prompt")}.

        Analyze only this chronological history ({history.Count} daily digits):
        {string.Join(",", history)}

        Predict the single number immediately after this history:
        1. Use only the history above. Never use the hidden actual number or any later value.
        2. Test sequence, transition, recency, frequency-gap, and position-in-{period} periodic/weekday rules.
        3. Return exactly three unique digits from 0 through 9, strongest first.
        4. Briefly state the winning rule and uncertainty. Do not claim guaranteed accuracy.
        """;

    private static int[] ParseSeries(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Series data is required.", nameof(source));
        }

        var values = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Any(value => value.Length != 1 || value[0] is < '0' or > '9'))
        {
            throw new ArgumentException("Series data must contain only comma-separated digits from 0 through 9.", nameof(source));
        }

        return values.Select(value => value[0] - '0').ToArray();
    }

    private static ModelEvaluation Backtest(int[] series, int period, int start, ModelWeights model)
    {
        var hits = 0;
        var reciprocalRankTotal = 0d;
        var checks = new List<WalkForwardCheck>();
        for (var index = start; index < series.Length; index++)
        {
            var history = GetHistoryWindow(series, index);
            var ranking = Rank(history, period, model);
            var rank = Array.FindIndex(ranking, item => item.Digit == series[index]);
            if (rank is >= 0 and < 3) hits++;
            reciprocalRankTotal += rank >= 0 ? 1d / (rank + 1) : 0;
            checks.Add(new WalkForwardCheck(
                index,
                ranking.Take(3).Select(item => item.Digit).ToArray(),
                series[index],
                rank is >= 0 and < 3));
        }

        var attempts = series.Length - start;
        return new ModelEvaluation(
            model,
            hits,
            attempts,
            attempts == 0 ? 0 : (double)hits / attempts,
            attempts == 0 ? 0 : reciprocalRankTotal / attempts,
            checks);
    }

    private static int[] GetHistoryWindow(IReadOnlyList<int> series, int targetIndex)
    {
        var historyStart = Math.Max(0, targetIndex - PromptHistoryCount);
        return series.Skip(historyStart).Take(targetIndex - historyStart).ToArray();
    }

    private static RankedDigit[] Rank(ReadOnlySpan<int> history, int period, ModelWeights weights)
    {
        var scores = new double[10];
        Add(scores, NGram(history), weights.NGram);
        Add(scores, Transitions(history), weights.Transition);
        Add(scores, Periodic(history, period), weights.Periodic);
        Add(scores, Recency(history), weights.Recency);
        Add(scores, Frequency(history), weights.Frequency);
        Add(scores, Gap(history), weights.Gap);

        return Enumerable.Range(0, 10)
            .Select(digit => new RankedDigit(digit, scores[digit]))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Digit)
            .ToArray();
    }

    private static double[] NGram(ReadOnlySpan<int> history)
    {
        var result = new double[10];
        var totalWeight = 0d;
        for (var order = Math.Min(4, history.Length - 1); order >= 1; order--)
        {
            var counts = Enumerable.Repeat(.35, 10).ToArray();
            var support = 0;
            for (var index = order; index < history.Length; index++)
            {
                var matches = true;
                for (var offset = 0; offset < order; offset++)
                {
                    if (history[index - order + offset] != history[history.Length - order + offset])
                    {
                        matches = false;
                        break;
                    }
                }
                if (!matches) continue;
                counts[history[index]]++;
                support++;
            }

            if (support == 0) continue;
            var orderWeight = order * Math.Log2(support + 1);
            Add(result, Normalize(counts), orderWeight);
            totalWeight += orderWeight;
        }

        return totalWeight == 0 ? Frequency(history) : result.Select(value => value / totalWeight).ToArray();
    }

    private static double[] Transitions(ReadOnlySpan<int> history)
    {
        var counts = Enumerable.Repeat(.5, 10).ToArray();
        var previous = history[^1];
        for (var index = 1; index < history.Length; index++)
        {
            if (history[index - 1] == previous) counts[history[index]]++;
        }
        return Normalize(counts);
    }

    private static double[] Periodic(ReadOnlySpan<int> history, int period)
    {
        var counts = Enumerable.Repeat(.5, 10).ToArray();
        var nextPosition = history.Length % period;
        for (var index = nextPosition; index < history.Length; index += period)
        {
            var age = history.Length - index;
            counts[history[index]] += Math.Pow(.985, age / (double)period);
        }
        return Normalize(counts);
    }

    private static double[] Recency(ReadOnlySpan<int> history)
    {
        var counts = Enumerable.Repeat(.25, 10).ToArray();
        for (var index = 0; index < history.Length; index++)
        {
            counts[history[index]] += Math.Pow(.94, history.Length - 1 - index);
        }
        return Normalize(counts);
    }

    private static double[] Frequency(ReadOnlySpan<int> history)
    {
        var counts = Enumerable.Repeat(1d, 10).ToArray();
        foreach (var digit in history) counts[digit]++;
        return Normalize(counts);
    }

    private static double[] Gap(ReadOnlySpan<int> history)
    {
        var values = new double[10];
        for (var digit = 0; digit < 10; digit++)
        {
            var last = -1;
            var occurrences = 0;
            for (var index = 0; index < history.Length; index++)
            {
                if (history[index] != digit) continue;
                last = index;
                occurrences++;
            }
            var gap = last < 0 ? history.Length : history.Length - 1 - last;
            var expectedGap = (history.Length + 10d) / (occurrences + 1d);
            values[digit] = .25 + Math.Min(3, gap / Math.Max(1, expectedGap));
        }
        return Normalize(values);
    }

    private static double[] Normalize(double[] values)
    {
        var total = values.Sum();
        return total <= 0 ? Enumerable.Repeat(.1, 10).ToArray() : values.Select(value => value / total).ToArray();
    }

    private static void Add(double[] target, double[] source, double weight)
    {
        for (var index = 0; index < target.Length; index++) target[index] += source[index] * weight;
    }

    private sealed record ModelWeights(
        string Name,
        double NGram,
        double Transition,
        double Periodic,
        double Recency,
        double Frequency,
        double Gap);

    private sealed record ModelEvaluation(
        ModelWeights Model,
        int Hits,
        int Attempts,
        double HitRate,
        double MeanReciprocalRank,
        IReadOnlyList<WalkForwardCheck> Checks);

    private sealed record WalkForwardCheck(
        int TargetIndex,
        IReadOnlyList<int> PredictedDigits,
        int ActualDigit,
        bool IsHit);

    private sealed record RankedDigit(int Digit, double Score);
}
