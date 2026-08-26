using PPG.GuessData.Models;

namespace PPG.GuessData;

public sealed class PanelAnalysisService : IPanelAnalysisService
{
    public PanelAnalysisResult Analyze(
        IReadOnlyList<Panel> panels,
        IReadOnlyList<string> availableDays,
        string numbers,
        PanelNumberType numberType = PanelNumberType.Open,
        PanelPatternType pattern = PanelPatternType.Sequence,
        int skipLastNumbers = 0)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(availableDays);

        if (!Enum.IsDefined(numberType))
        {
            throw new ArgumentOutOfRangeException(nameof(numberType), "Select Open or Close.");
        }

        if (!Enum.IsDefined(pattern))
        {
            throw new ArgumentOutOfRangeException(nameof(pattern), "Select a valid pattern.");
        }

        if (skipLastNumbers is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skipLastNumbers),
                "Skip Last Number must be between 0 and 4.");
        }

        var days = availableDays
            .Where(day => !string.IsNullOrWhiteSpace(day))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (days.Length == 0)
        {
            throw new ArgumentException("The selected game does not contain any complete day columns.", nameof(availableDays));
        }

        var patternDay = GetPatternDay(pattern);
        if (patternDay is not null
            && !days.Contains(patternDay, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The selected game does not contain {pattern} data.",
                nameof(pattern));
        }

        var currentData = BuildCurrentData(panels, days, numberType);
        var panelRows = BuildPanelRows(panels, days);
        var latestNumbers = currentData
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.Number) &&
                row.Number != "*")
            .Select(row => row.Number)
            .ToArray();
        var calculationData = SkipLatestNumbers(currentData, skipLastNumbers);
        var currentDataWeeks = BuildCurrentDataWeeks(calculationData, days.Length);
        var patternData = patternDay is null
            ? calculationData
            : calculationData
                .Where(row => string.Equals(
                    row.DayOfWeek,
                    patternDay,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var targetSeries = ResolveTargetSeries(numbers, patternData);
        IReadOnlyList<MatchLine> matchLines;
        IReadOnlyList<int> matchingRowIds;
        ThreeTouchAnalysis? threeTouch = null;
        if (pattern == PanelPatternType.ThreeTouch)
        {
            matchLines = FindThreeTouchForecast(
                calculationData,
                targetSeries,
                numberType,
                days.Length,
                out matchingRowIds,
                out threeTouch);
        }
        else if (pattern == PanelPatternType.AI)
        {
            matchLines = FindAiMatches(
                calculationData,
                days,
                targetSeries,
                days.Length,
                out matchingRowIds);
        }
        else if (pattern == PanelPatternType.Cross)
        {
            matchLines = FindCrossMatches(
                calculationData,
                targetSeries,
                days.Length,
                out matchingRowIds);
        }
        else if (pattern == PanelPatternType.Weekly)
        {
            matchLines = FindWeeklyMatches(
                calculationData,
                targetSeries,
                days.Length,
                out matchingRowIds);
        }
        else
        {
            matchLines = FindMatches(
                calculationData,
                patternData,
                targetSeries,
                days.Length,
                out matchingRowIds);
        }
        var nextNumberCounts = matchLines
            .GroupBy(match => match.NextNumber, StringComparer.Ordinal)
            .Select(group => new NextNumberCount
            {
                Number = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Number, StringComparer.Ordinal)
            .ToArray();
        var countsByNumber = nextNumberCounts.ToDictionary(
            item => item.Number,
            item => item.Count,
            StringComparer.Ordinal);
        matchLines = matchLines
            .OrderByDescending(match => countsByNumber[match.NextNumber])
            .ThenBy(match => match.NextNumber, StringComparer.Ordinal)
            .ToArray();

        return new PanelAnalysisResult
        {
            Pattern = pattern,
            NumberType = numberType,
            GuessNumbers = string.Join(',', targetSeries),
            LatestNumbers = latestNumbers,
            AvailableDays = days,
            CurrentData = calculationData,
            CurrentDataWeeks = currentDataWeeks,
            MatchingRowIds = matchingRowIds,
            MatchLines = matchLines,
            NextNumberCounts = nextNumberCounts,
            ThreeTouch = threeTouch,
            Panels = panels,
            PanelRows = panelRows
        };
    }

    private static string[] ResolveTargetSeries(
        string numbers,
        IReadOnlyList<CurrentDataRow> currentData)
    {
        if (string.IsNullOrWhiteSpace(numbers))
        {
            var latestNumbers = currentData
                .Where(row => !string.IsNullOrEmpty(row.Number) && row.Number != "*")
                .Select(row => row.Number)
                .TakeLast(3)
                .ToArray();

            if (latestNumbers.Length < 3)
            {
                throw new ArgumentException("At least three panel numbers are required.", nameof(numbers));
            }

            return latestNumbers;
        }

        var targetSeries = numbers
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value != "*")
            .ToArray();

        if (targetSeries.Length == 0 || targetSeries.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Enter at least one comma-separated number.", nameof(numbers));
        }

        return targetSeries;
    }

    private static IReadOnlyList<CurrentDataRow> SkipLatestNumbers(
        IReadOnlyList<CurrentDataRow> rows,
        int skipLastNumbers)
    {
        if (skipLastNumbers == 0)
        {
            return rows;
        }

        var validNumberIndexes = rows
            .Select((row, index) => (row, index))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.row.Number) &&
                item.row.Number != "*")
            .Select(item => item.index)
            .ToArray();

        if (validNumberIndexes.Length <= skipLastNumbers)
        {
            throw new ArgumentException(
                "Skip Last Number leaves no historical data to analyze.",
                nameof(skipLastNumbers));
        }

        var firstSkippedNumberIndex = validNumberIndexes[^skipLastNumbers];
        return rows.Take(firstSkippedNumberIndex).ToArray();
    }

    private static IReadOnlyList<CurrentDataRow> BuildCurrentData(
        IReadOnlyList<Panel> panels,
        IReadOnlyList<string> days,
        PanelNumberType numberType)
    {
        var currentData = new List<CurrentDataRow>(panels.Count * days.Count);
        var id = 1;

        foreach (var panel in panels)
        {
            for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
            {
                var day = days[dayIndex];
                var value = panel.GetValue(day);
                currentData.Add(new CurrentDataRow
                {
                    Id = id++,
                    DayOfWeek = day,
                    Number = GetPairDigit(value, numberType),
                    WeekDate = dayIndex == 0 ? panel.WeekDate : string.Empty
                });
            }
        }

        return currentData;
    }

    private static string GetPairDigit(string value, PanelNumberType numberType)
    {
        var digitIndex = numberType == PanelNumberType.Close ? 1 : 0;
        return value.Length > digitIndex ? value.Substring(digitIndex, 1) : string.Empty;
    }

    private static IReadOnlyList<CurrentDataWeek> BuildCurrentDataWeeks(
        IReadOnlyList<CurrentDataRow> currentData,
        int dayCount)
    {
        var weeks = new List<CurrentDataWeek>();

        for (var index = 0; index < currentData.Count; index += dayCount)
        {
            var weekRows = currentData.Skip(index).Take(dayCount).ToArray();
            if (weekRows.Length == 0)
            {
                continue;
            }

            weeks.Add(new CurrentDataWeek
            {
                Id = weekRows[0].Id,
                WeekDate = weekRows[0].WeekDate,
                Days = weekRows.ToDictionary(
                    row => row.DayOfWeek,
                    row => row,
                    StringComparer.OrdinalIgnoreCase)
            });
        }

        weeks.Reverse();
        return weeks;
    }

    private static IReadOnlyList<PanelDisplayRow> BuildPanelRows(
        IReadOnlyList<Panel> panels,
        IReadOnlyList<string> days)
    {
        return panels
            .Select((panel, index) => new PanelDisplayRow
            {
                Id = index + 1,
                WeekDate = panel.WeekDate,
                Days = days.ToDictionary(
                    day => day,
                    day =>
                    {
                        var pair = panel.GetValue(day);
                        return new PanelDayValue
                        {
                            Open = panel.GetValue($"{day}_OPEN"),
                            Pair = pair,
                            Close = panel.GetValue($"{day}_CLOSE"),
                            IsRedPair = RedPairValues.Contains(pair)
                        };
                    },
                    StringComparer.OrdinalIgnoreCase)
            })
            .Reverse()
            .ToArray();
    }

    private static IReadOnlyList<MatchLine> FindMatches(
        IReadOnlyList<CurrentDataRow> currentData,
        IReadOnlyList<CurrentDataRow> patternData,
        IReadOnlyList<string> targetSeries,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds)
    {
        var matches = new List<MatchLine>();
        var matchedIds = new HashSet<int>();
        var searchableData = patternData
            .Where(row => IsSearchableNumber(row.Number))
            .ToArray();

        for (var startIndex = 0; startIndex <= searchableData.Length - targetSeries.Count; startIndex++)
        {
            var found = true;
            for (var targetIndex = 0; targetIndex < targetSeries.Count; targetIndex++)
            {
                if (!string.Equals(
                        searchableData[startIndex + targetIndex].Number,
                        targetSeries[targetIndex],
                        StringComparison.Ordinal))
                {
                    found = false;
                    break;
                }
            }

            if (!found)
            {
                continue;
            }

            for (var targetIndex = 0; targetIndex < targetSeries.Count; targetIndex++)
            {
                matchedIds.Add(searchableData[startIndex + targetIndex].Id);
            }

            var nextIndex = startIndex + targetSeries.Count;
            if (nextIndex >= searchableData.Length)
            {
                continue;
            }

            var nextNumber = searchableData[nextIndex].Number;
            if (string.IsNullOrEmpty(nextNumber))
            {
                continue;
            }

            var matchedRow = searchableData[startIndex];
            var rawStartIndex = matchedRow.Id - 1;

            matches.Add(new MatchLine
            {
                CurrentDataRowId = matchedRow.Id,
                WeekDate = currentData[rawStartIndex - (rawStartIndex % dayCount)].WeekDate,
                NextNumber = nextNumber
            });
        }

        matchingRowIds = matchedIds.OrderBy(id => id).ToArray();

        return matches
            .OrderByDescending(match => match.NextNumber, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<MatchLine> FindCrossMatches(
        IReadOnlyList<CurrentDataRow> currentData,
        IReadOnlyList<string> targetSeries,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds)
    {
        return FindPathMatches(
            currentData,
            BuildCrossPaths(currentData, dayCount),
            targetSeries,
            dayCount,
            out matchingRowIds);
    }

    private static IReadOnlyList<MatchLine> FindThreeTouchForecast(
        IReadOnlyList<CurrentDataRow> currentData,
        IReadOnlyList<string> targetSeries,
        PanelNumberType numberType,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds,
        out ThreeTouchAnalysis analysis)
    {
        var searchableData = currentData
            .Where(row => IsSearchableNumber(row.Number))
            .ToArray();
        var matchingStartIndexes = new List<int>();

        for (var startIndex = 0;
             startIndex <= searchableData.Length - targetSeries.Count;
             startIndex++)
        {
            var isMatch = true;
            for (var targetIndex = 0; targetIndex < targetSeries.Count; targetIndex++)
            {
                if (!string.Equals(
                        searchableData[startIndex + targetIndex].Number,
                        targetSeries[targetIndex],
                        StringComparison.Ordinal))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                matchingStartIndexes.Add(startIndex);
            }
        }

        var latestAnchorDay = matchingStartIndexes.Count > 0
            ? searchableData[matchingStartIndexes[^1]].DayOfWeek
            : string.Empty;
        var sameDayStartIndexes = matchingStartIndexes
            .Where(startIndex => string.Equals(
                searchableData[startIndex].DayOfWeek,
                latestAnchorDay,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var completedSameDayTouchCount = sameDayStartIndexes.Count(
            startIndex => startIndex + targetSeries.Count < searchableData.Length);
        var useSameDayHistory = completedSameDayTouchCount >= 3;
        var anchorDay = useSameDayHistory ? latestAnchorDay : "Multiple days";
        IReadOnlyList<int> touchStartIndexes = useSameDayHistory
            ? sameDayStartIndexes
            : matchingStartIndexes;
        var occurrences = touchStartIndexes
            .Where(startIndex => startIndex + targetSeries.Count < searchableData.Length)
            .Select(startIndex =>
            {
                var matchedRow = searchableData[startIndex];
                var rawStartIndex = matchedRow.Id - 1;
                return new ThreeTouchOccurrence(
                    matchedRow.Id,
                    currentData[rawStartIndex - (rawStartIndex % dayCount)].WeekDate,
                    searchableData[startIndex + targetSeries.Count].Number,
                    searchableData
                        .Skip(startIndex)
                        .Take(targetSeries.Count)
                        .Select(row => row.Id)
                        .ToArray());
            })
            .OrderBy(occurrence => occurrence.AnchorRowId)
            .ToArray();
        var latestTouches = occurrences.TakeLast(3).ToArray();
        var touchPoints = latestTouches
            .Select((touch, index) => new ThreeTouchPoint
            {
                Label = $"T{index + 1}",
                AnchorRowId = touch.AnchorRowId,
                WeekDate = touch.WeekDate,
                Outcome = touch.Outcome
            })
            .ToArray();
        var anchorSequence = string.Join(',', targetSeries);

        matchingRowIds = latestTouches
            .SelectMany(touch => touch.MatchingRowIds)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (latestTouches.Length < 3
            || latestTouches.Any(touch => !int.TryParse(touch.Outcome, out _)))
        {
            analysis = new ThreeTouchAnalysis
            {
                AnchorSequence = anchorSequence,
                AnchorDay = anchorDay,
                Touches = touchPoints,
                RuleDescription = "At least three completed numeric touches on the same anchor day are required.",
                Prompt = BuildThreeTouchPrompt(
                    anchorSequence,
                    anchorDay,
                    numberType,
                    touchPoints,
                    "Insufficient history",
                    "Collect at least three completed touches before forecasting.",
                    string.Empty)
            };
            return [];
        }

        var outcomes = occurrences
            .Select(occurrence => int.TryParse(occurrence.Outcome, out var value) ? value : -1)
            .Where(value => value >= 0)
            .ToArray();
        var latestOutcomes = latestTouches.Select(touch => int.Parse(touch.Outcome)).ToArray();
        var firstDelta = Mod10(latestOutcomes[1] - latestOutcomes[0]);
        var secondDelta = Mod10(latestOutcomes[2] - latestOutcomes[1]);
        var stepChange = Mod10(secondDelta - firstDelta);

        var rules = new[]
        {
            new ThreeTouchRule(
                "Fixed Step",
                (first, second, third) => Mod10(third + Mod10(third - second)),
                firstDelta == secondDelta ? 0 : 1),
            new ThreeTouchRule(
                "Progressive Step",
                (first, second, third) =>
                {
                    var earlierStep = Mod10(second - first);
                    var recentStep = Mod10(third - second);
                    return Mod10(third + recentStep + Mod10(recentStep - earlierStep));
                },
                firstDelta == secondDelta ? 1 : 0),
            new ThreeTouchRule(
                "Cut Digit",
                (_first, _second, third) => Mod10(third + 5),
                2)
        };
        var attempts = Math.Max(0, outcomes.Length - 3);
        var selectedRule = rules
            .Select(rule => new
            {
                Rule = rule,
                Wins = Enumerable.Range(3, attempts).Count(index =>
                    rule.Predict(
                        outcomes[index - 3],
                        outcomes[index - 2],
                        outcomes[index - 1]) == outcomes[index])
            })
            .OrderByDescending(result => result.Wins)
            .ThenBy(result => result.Rule.TiePriority)
            .First();
        var predictedNumber = selectedRule.Rule.Predict(
            latestOutcomes[0],
            latestOutcomes[1],
            latestOutcomes[2]).ToString();
        var ruleDescription = selectedRule.Rule.Name switch
        {
            "Fixed Step" => $"Repeat the latest modular step +{secondDelta} after T3.",
            "Progressive Step" =>
                $"Touch steps are +{firstDelta} then +{secondDelta}; continue the +{stepChange} step shift.",
            _ => "Apply the cut/complement conversion (+5 modulo 10) to T3."
        };

        analysis = new ThreeTouchAnalysis
        {
            AnchorSequence = anchorSequence,
            AnchorDay = anchorDay,
            Touches = touchPoints,
            RuleName = selectedRule.Rule.Name,
            RuleDescription = ruleDescription,
            PredictedNumber = predictedNumber,
            BacktestWins = selectedRule.Wins,
            BacktestAttempts = attempts,
            Prompt = BuildThreeTouchPrompt(
                anchorSequence,
                anchorDay,
                numberType,
                touchPoints,
                selectedRule.Rule.Name,
                ruleDescription,
                predictedNumber)
        };

        var latestTouch = latestTouches[^1];
        return
        [
            new MatchLine
            {
                CurrentDataRowId = latestTouch.AnchorRowId,
                WeekDate = latestTouch.WeekDate,
                NextNumber = predictedNumber
            }
        ];
    }

    private static string BuildThreeTouchPrompt(
        string anchorSequence,
        string anchorDay,
        PanelNumberType numberType,
        IReadOnlyList<ThreeTouchPoint> touches,
        string ruleName,
        string ruleDescription,
        string predictedNumber)
    {
        var touchChain = touches.Count == 0
            ? "No completed touches found"
            : string.Join(
                " | ",
                touches.Select(touch =>
                    $"{touch.Label}: {touch.WeekDate} -> {touch.Outcome}"));
        var prediction = string.IsNullOrEmpty(predictedNumber)
            ? "Not enough history"
            : predictedNumber;

        return $"""
            Role: You are a 3-Touch Pattern Recognition and Sequential Forecasting Specialist.
            Analyze the available {numberType} digit history using the following verified result.
            Anchor sequence: {anchorSequence}
            Anchor day: {anchorDay}
            Pattern chain: {touchChain}
            Selected rule: {ruleName}
            Rule logic: {ruleDescription}
            Derived target: {prediction}
            Explain the T1 -> T2 -> T3 progression, show all modulo-10 steps, validate the selected rule against older touches, and clearly separate historical evidence from the forecast.
            """;
    }

    private static int Mod10(int value) => ((value % 10) + 10) % 10;

    private static IReadOnlyList<MatchLine> FindAiMatches(
        IReadOnlyList<CurrentDataRow> currentData,
        IReadOnlyList<string> availableDays,
        IReadOnlyList<string> targetSeries,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds)
    {
        var candidates = new List<(
            IReadOnlyList<MatchLine> Matches,
            IReadOnlyList<int> MatchingRowIds)>();

        var sequenceMatches = FindMatches(
            currentData,
            currentData,
            targetSeries,
            dayCount,
            out var sequenceRowIds);
        candidates.Add((sequenceMatches, sequenceRowIds));

        var crossMatches = FindCrossMatches(
            currentData,
            targetSeries,
            dayCount,
            out var crossRowIds);
        candidates.Add((crossMatches, crossRowIds));

        var weeklyMatches = FindWeeklyMatches(
            currentData,
            targetSeries,
            dayCount,
            out var weeklyRowIds);
        candidates.Add((weeklyMatches, weeklyRowIds));

        foreach (var day in availableDays)
        {
            var dayData = currentData
                .Where(row => string.Equals(
                    row.DayOfWeek,
                    day,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var dayMatches = FindMatches(
                currentData,
                dayData,
                targetSeries,
                dayCount,
                out var dayRowIds);
            candidates.Add((dayMatches, dayRowIds));
        }

        var uniqueMatches = candidates
            .SelectMany(candidate => candidate.Matches)
            .GroupBy(match => new
            {
                match.CurrentDataRowId,
                match.NextNumber,
                match.WeekDate
            })
            .Select(group => group.First())
            .ToArray();

        var bestNumber = uniqueMatches
            .GroupBy(match => match.NextNumber, StringComparer.Ordinal)
            .Select(group => new
            {
                Number = group.Key,
                Count = group.Count(),
                MostRecentRowId = group.Max(match => match.CurrentDataRowId)
            })
            .OrderByDescending(result => result.Count)
            .ThenByDescending(result => result.MostRecentRowId)
            .ThenBy(result => result.Number, StringComparer.Ordinal)
            .Select(result => result.Number)
            .FirstOrDefault();

        if (bestNumber is null)
        {
            matchingRowIds = [];
            return [];
        }

        matchingRowIds = candidates
            .Where(candidate => candidate.Matches.Any(match => string.Equals(
                match.NextNumber,
                bestNumber,
                StringComparison.Ordinal)))
            .SelectMany(candidate => candidate.MatchingRowIds)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        return uniqueMatches
            .Where(match => string.Equals(
                match.NextNumber,
                bestNumber,
                StringComparison.Ordinal))
            .OrderByDescending(match => match.CurrentDataRowId)
            .ToArray();
    }

    private static IReadOnlyList<MatchLine> FindWeeklyMatches(
        IReadOnlyList<CurrentDataRow> currentData,
        IReadOnlyList<string> targetSeries,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds)
    {
        var weeklyPaths = currentData
            .Select((row, index) => (row, index))
            .GroupBy(item => item.index / dayCount)
            .Select(group => (IReadOnlyList<CurrentDataRow>)group
                .Select(item => item.row)
                .ToArray());

        return FindPathMatches(
            currentData,
            weeklyPaths,
            targetSeries,
            dayCount,
            out matchingRowIds);
    }

    private static IReadOnlyList<MatchLine> FindPathMatches(
        IReadOnlyList<CurrentDataRow> currentData,
        IEnumerable<IReadOnlyList<CurrentDataRow>> paths,
        IReadOnlyList<string> targetSeries,
        int dayCount,
        out IReadOnlyList<int> matchingRowIds)
    {
        var matches = new List<MatchLine>();
        var matchedIds = new HashSet<int>();

        foreach (var path in paths)
        {
            var searchablePath = path
                .Where(row => IsSearchableNumber(row.Number))
                .ToArray();

            for (var startIndex = 0;
                 startIndex <= searchablePath.Length - targetSeries.Count;
                 startIndex++)
            {
                var found = true;
                for (var targetIndex = 0; targetIndex < targetSeries.Count; targetIndex++)
                {
                    if (!string.Equals(
                            searchablePath[startIndex + targetIndex].Number,
                            targetSeries[targetIndex],
                            StringComparison.Ordinal))
                    {
                        found = false;
                        break;
                    }
                }

                if (!found)
                {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < targetSeries.Count; targetIndex++)
                {
                    matchedIds.Add(searchablePath[startIndex + targetIndex].Id);
                }

                var nextIndex = startIndex + targetSeries.Count;
                if (nextIndex >= searchablePath.Length)
                {
                    continue;
                }

                var nextNumber = searchablePath[nextIndex].Number;
                if (string.IsNullOrEmpty(nextNumber))
                {
                    continue;
                }

                var matchedRow = searchablePath[startIndex];
                var rawStartIndex = matchedRow.Id - 1;
                matches.Add(new MatchLine
                {
                    CurrentDataRowId = matchedRow.Id,
                    WeekDate = currentData[rawStartIndex - (rawStartIndex % dayCount)].WeekDate,
                    NextNumber = nextNumber
                });
            }
        }

        matchingRowIds = matchedIds.OrderBy(id => id).ToArray();

        return matches
            .OrderByDescending(match => match.NextNumber, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSearchableNumber(string number) =>
        !string.IsNullOrWhiteSpace(number) && number != "*";

    private static IEnumerable<IReadOnlyList<CurrentDataRow>> BuildCrossPaths(
        IReadOnlyList<CurrentDataRow> currentData,
        int dayCount)
    {
        var weekCount = (int)Math.Ceiling((double)currentData.Count / dayCount);
        foreach (var dayStep in new[] { 1, -1 })
        {
            for (var dayIndex = 0; dayIndex < dayCount; dayIndex++)
            {
                yield return BuildCrossPath(
                    currentData,
                    dayCount,
                    weekCount,
                    startWeekIndex: 0,
                    startDayIndex: dayIndex,
                    dayStep);
            }

            var boundaryDayIndex = dayStep > 0 ? 0 : dayCount - 1;
            for (var weekIndex = 1; weekIndex < weekCount; weekIndex++)
            {
                yield return BuildCrossPath(
                    currentData,
                    dayCount,
                    weekCount,
                    startWeekIndex: weekIndex,
                    startDayIndex: boundaryDayIndex,
                    dayStep);
            }
        }
    }

    private static IReadOnlyList<CurrentDataRow> BuildCrossPath(
        IReadOnlyList<CurrentDataRow> currentData,
        int dayCount,
        int weekCount,
        int startWeekIndex,
        int startDayIndex,
        int dayStep)
    {
        var path = new List<CurrentDataRow>(dayCount);
        var weekIndex = startWeekIndex;
        var dayIndex = startDayIndex;

        while (weekIndex < weekCount && dayIndex >= 0 && dayIndex < dayCount)
        {
            var rowIndex = (weekIndex * dayCount) + dayIndex;
            if (rowIndex >= currentData.Count)
            {
                break;
            }

            path.Add(currentData[rowIndex]);
            weekIndex++;
            dayIndex += dayStep;
        }

        return path;
    }

    private static string? GetPatternDay(PanelPatternType pattern) => pattern switch
    {
        PanelPatternType.AI => null,
        PanelPatternType.ThreeTouch => null,
        PanelPatternType.Sequence => null,
        PanelPatternType.Cross => null,
        PanelPatternType.Weekly => null,
        PanelPatternType.Monday => "MON",
        PanelPatternType.Tuesday => "TUE",
        PanelPatternType.Wednesday => "WED",
        PanelPatternType.Thursday => "THU",
        PanelPatternType.Friday => "FRI",
        PanelPatternType.Saturday => "SAT",
        PanelPatternType.Sunday => "SUN",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Select a valid pattern.")
    };

    private static readonly HashSet<string> RedPairValues = new(StringComparer.Ordinal)
    {
        "00", "05", "11", "16", "22", "27", "33", "38", "44", "49",
        "50", "55", "61", "66", "72", "77", "83", "88", "94", "99"
    };

    private sealed record ThreeTouchOccurrence(
        int AnchorRowId,
        string WeekDate,
        string Outcome,
        IReadOnlyList<int> MatchingRowIds);

    private sealed record ThreeTouchRule(
        string Name,
        Func<int, int, int, int> Predict,
        int TiePriority);
}
