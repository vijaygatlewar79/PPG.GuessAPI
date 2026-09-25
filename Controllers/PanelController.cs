using Microsoft.AspNetCore.Mvc;
using PPG.GuessData;
using PPG.GuessData.Models;

namespace PPG.GuessAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PanelController : ControllerBase
{
    private readonly IExcelReaderService _excelReaderService;
    private readonly IPanelAnalysisService _panelAnalysisService;
    private readonly IPanelGameService _panelGameService;
    private readonly IPanelFileStorage _fileStorage;
    private readonly ChartSourceCatalog _chartSourceCatalog;

    public PanelController(
        IExcelReaderService excelReaderService,
        IPanelAnalysisService panelAnalysisService,
        IPanelGameService panelGameService,
        IPanelFileStorage fileStorage,
        ChartSourceCatalog chartSourceCatalog)
    {
        _excelReaderService = excelReaderService;
        _panelAnalysisService = panelAnalysisService;
        _panelGameService = panelGameService;
        _fileStorage = fileStorage;
        _chartSourceCatalog = chartSourceCatalog;
    }

    [HttpGet("games")]
    [ProducesResponseType<IReadOnlyList<PanelGame>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PanelGame>>> GetGames(
        CancellationToken cancellationToken)
    {
        var games = await _panelGameService.GetAvailableGamesAsync(cancellationToken);
        var options = await _chartSourceCatalog.GetOptionsAsync(cancellationToken);
        var sourcesByFileName = options.Sources.ToDictionary(
            source => source.FileName,
            StringComparer.OrdinalIgnoreCase);

        return Ok(games.Select(game =>
        {
            sourcesByFileName.TryGetValue(game.FileName, out var source);
            return new PanelGame
            {
                FileName = game.FileName,
                DisplayName = source?.DisplayName ?? game.DisplayName,
                OrderBy = source?.OrderBy ?? game.OrderBy,
                SourceUrl = source?.Url ?? game.SourceUrl
            };
        })
        .OrderBy(game => game.OrderBy)
        .ThenBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(game => game.FileName, StringComparer.OrdinalIgnoreCase)
        .ToArray());
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<Panel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<Panel>>> Get(
        [FromQuery] string? fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolvedFileName = await _panelGameService.ResolveGameFileNameAsync(
                fileName,
                cancellationToken);
            await using var workbookStream = await _fileStorage.OpenExcelFileAsync(
                resolvedFileName,
                cancellationToken);
            var workbook = await _excelReaderService.ReadPanelsAsync(
                workbookStream,
                cancellationToken);
            return Ok(workbook.Panels);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(fileName), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (FileNotFoundException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected game file was not found.",
                detail: exception.Message);
        }
    }

    [HttpPost("analyze")]
    [ProducesResponseType<PanelAnalysisResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PanelAnalysisResult>> Analyze(
        [FromBody] PanelAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        PanelAnalysisResult result;
        try
        {
            var fileName = await _panelGameService.ResolveGameFileNameAsync(
                request.FileName,
                cancellationToken);
            await using var workbookStream = await _fileStorage.OpenExcelFileAsync(
                fileName,
                cancellationToken);
            var workbook = await _excelReaderService.ReadPanelsAsync(
                workbookStream,
                cancellationToken);
            result = _panelAnalysisService.Analyze(
                workbook.Panels,
                workbook.AvailableDays,
                request.Numbers,
                request.NumberType,
                request.Pattern,
                request.SkipLastNumbers);
        }
        catch (ArgumentException exception)
        {
            var fieldName = exception.ParamName switch
            {
                "pattern" => nameof(request.Pattern),
                "numberType" => nameof(request.NumberType),
                "skipLastNumbers" => nameof(request.SkipLastNumbers),
                "fileName" => nameof(request.FileName),
                _ => nameof(request.Numbers)
            };
            ModelState.AddModelError(fieldName, exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidDataException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (IOException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), $"The selected game file could not be read: {exception.Message}");
            return ValidationProblem(ModelState);
        }

        return Ok(result);
    }

    [HttpPost("analyze-last-week")]
    [ProducesResponseType<IReadOnlyList<LastWeekAnalysisRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<LastWeekAnalysisRow>>> AnalyzeLastWeek(
        [FromBody] LastWeekAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.LatestCount is < 1 or > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.LatestCount),
                    "Latest must be between 1 and 4.");
            }

            if (request.SkipLastNumbers is < 0 or > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.SkipLastNumbers),
                    "Skip Last Number must be between 0 and 4.");
            }

            if (request.TopCount is < 1 or > 10)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.TopCount),
                    "Top count must be between 1 and 10.");
            }

            if (request.DayCount is < 1 or > 30)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.DayCount),
                    "Show Last Day Result must be between 1 and 30 days.");
            }

            var patterns = request.Patterns
                .Distinct()
                .ToArray();
            if (patterns.Length == 0)
            {
                throw new ArgumentException(
                    "Select at least one panel pattern.",
                    nameof(request.Patterns));
            }

            var fileName = await _panelGameService.ResolveGameFileNameAsync(
                request.FileName,
                cancellationToken);
            await using var workbookStream = await _fileStorage.OpenExcelFileAsync(
                fileName,
                cancellationToken);
            var workbook = await _excelReaderService.ReadPanelsAsync(
                workbookStream,
                cancellationToken);
            var availableDays = workbook.AvailableDays
                .Where(day => !string.IsNullOrWhiteSpace(day))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var fullSeed = _panelAnalysisService.Analyze(
                workbook.Panels,
                workbook.AvailableDays,
                string.Empty,
                request.NumberType,
                PanelPatternType.Sequence);
            var fullValidRows = fullSeed.CurrentData
                .Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*")
                .ToArray();
            var latestDataDayIndex = Array.FindIndex(
                availableDays,
                day => string.Equals(
                    day,
                    fullValidRows[^1].DayOfWeek,
                    StringComparison.OrdinalIgnoreCase));
            var nextUnreportedDay = availableDays[
                (latestDataDayIndex + 1) % availableDays.Length];
            var rows = new List<LastWeekAnalysisRow>(request.DayCount);

            for (var rowOffset = 0; rowOffset < request.DayCount; rowOffset++)
            {
                var skipCount = request.SkipLastNumbers + rowOffset;
                var seed = skipCount == 0
                    ? fullSeed
                    : _panelAnalysisService.Analyze(
                        workbook.Panels,
                        workbook.AvailableDays,
                        string.Empty,
                        request.NumberType,
                        PanelPatternType.Sequence,
                        skipCount);
                var validRows = seed.CurrentData
                    .Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*")
                    .ToArray();
                var guessNumbers = string.Join(",", validRows
                    .TakeLast(request.LatestCount)
                    .Select(row => row.Number));
                var totals = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var pattern in patterns)
                {
                    var analysis = _panelAnalysisService.Analyze(
                        workbook.Panels,
                        workbook.AvailableDays,
                        guessNumbers,
                        request.NumberType,
                        pattern,
                        skipCount);
                    foreach (var count in analysis.NextNumberCounts)
                    {
                        totals[count.Number] = totals.GetValueOrDefault(count.Number) + count.Count;
                    }
                }

                var passRow = skipCount > 0
                    ? fullValidRows[^skipCount]
                    : null;

                rows.Add(new LastWeekAnalysisRow
                {
                    DayGuess = passRow?.DayOfWeek ?? nextUnreportedDay,
                    Numbers = totals
                        .OrderByDescending(item => item.Value)
                        .ThenBy(item => item.Key, StringComparer.Ordinal)
                        .Take(request.TopCount)
                        .Select(item => item.Key)
                        .ToArray(),
                    PassNumber = passRow?.Number ?? string.Empty
                });
            }

            return Ok(rows);
        }
        catch (ArgumentException exception)
        {
            var fieldName = exception.ParamName switch
            {
                "numberType" => nameof(request.NumberType),
                "fileName" => nameof(request.FileName),
                nameof(request.LatestCount) => nameof(request.LatestCount),
                nameof(request.TopCount) => nameof(request.TopCount),
                nameof(request.DayCount) => nameof(request.DayCount),
                nameof(request.SkipLastNumbers) => nameof(request.SkipLastNumbers),
                nameof(request.Patterns) => nameof(request.Patterns),
                _ => nameof(request.Patterns)
            };
            ModelState.AddModelError(fieldName, exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidDataException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (IOException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), $"The selected game file could not be read: {exception.Message}");
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("analyze-pattern-wise")]
    [HttpPost("analyze-pattern-wise-triple")]
    [ProducesResponseType<IReadOnlyList<PatternWiseAnalysisRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PatternWiseAnalysisRow>>> AnalyzePatternWise(
        [FromBody] PatternWiseAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.LatestCount is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(request.LatestCount), "Latest must be between 1 and 4.");
            if (request.SkipLastNumbers is < 0 or > 7)
                throw new ArgumentOutOfRangeException(nameof(request.SkipLastNumbers), "Skip Last Number must be between 0 and 7.");
            if (request.TopCount is < 1 or > 10)
                throw new ArgumentOutOfRangeException(nameof(request.TopCount), "Top count must be between 1 and 10.");
            if (request.DayCount is < 1 or > 365)
                throw new ArgumentOutOfRangeException(nameof(request.DayCount), "Day count must be between 1 and 365.");

            var patterns = request.Patterns.Distinct().ToArray();
            if (patterns.Length == 0)
                throw new ArgumentException("Select at least one panel pattern.", nameof(request.Patterns));

            var fileName = await _panelGameService.ResolveGameFileNameAsync(request.FileName, cancellationToken);
            await using var workbookStream = await _fileStorage.OpenExcelFileAsync(fileName, cancellationToken);
            var workbook = await _excelReaderService.ReadPanelsAsync(workbookStream, cancellationToken);
            var fullSeed = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, request.NumberType, PanelPatternType.Sequence, useTripleNumbers: request.UseTripleNumbers);
            var validRows = fullSeed.CurrentData
                .Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*")
                .ToArray();
            if (validRows.Length == 0)
            {
                var columnType = request.NumberType == PanelNumberType.Open ? "OPEN" : "CLOSE";
                throw new ArgumentException(
                    request.UseTripleNumbers
                        ? $"The selected game does not contain three-digit values in its *_{columnType} columns."
                        : "The selected game does not contain valid panel values.",
                    nameof(request.FileName));
            }
            var availableDays = workbook.AvailableDays
                .Where(day => !string.IsNullOrWhiteSpace(day))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var latestDayIndex = Array.FindIndex(availableDays, day => string.Equals(
                day, validRows[^1].DayOfWeek, StringComparison.OrdinalIgnoreCase));
            var todayGuessDay = availableDays[(latestDayIndex + 1) % availableDays.Length];
            var currentGuessNumbers = string.Join(",", validRows
                .TakeLast(request.LatestCount)
                .Select(row => row.Number));

            var output = new List<PatternWiseAnalysisRow>(patterns.Length);
            foreach (var pattern in patterns)
            {
                var todayNumbers = _panelAnalysisService.Analyze(
                    workbook.Panels, workbook.AvailableDays, currentGuessNumbers,
                    request.NumberType, pattern, useTripleNumbers: request.UseTripleNumbers)
                    .NextNumberCounts
                    .OrderByDescending(item => item.Count)
                    .ThenBy(item => item.Number, StringComparer.Ordinal)
                    .Take(request.TopCount)
                    .Select(item => item.Number)
                    .ToArray();
                var results = new List<PatternWiseDayResult>(request.DayCount);
                for (var offset = 1; offset <= request.DayCount; offset++)
                {
                    var skipCount = request.SkipLastNumbers + offset;
                    if (skipCount > validRows.Length) break;
                    var seed = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, request.NumberType, PanelPatternType.Sequence, skipCount, request.UseTripleNumbers);
                    var guessNumbers = string.Join(",", seed.CurrentData
                        .Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*")
                        .TakeLast(request.LatestCount)
                        .Select(row => row.Number));
                    var counts = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, guessNumbers, request.NumberType, pattern, skipCount, request.UseTripleNumbers)
                        .NextNumberCounts
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => item.Number, StringComparer.Ordinal)
                        .Take(request.TopCount)
                        .Select(item => item.Number)
                        .ToArray();
                    var pass = validRows[^skipCount];
                    var rank = Array.FindIndex(counts, value => value == pass.Number);
                    results.Add(new PatternWiseDayResult
                    {
                        DayGuess = pass.DayOfWeek,
                        Numbers = counts,
                        PassNumber = pass.Number,
                        MatchRank = rank >= 0 ? rank + 1 : null
                    });
                }

                output.Add(new PatternWiseAnalysisRow
                {
                    Pattern = pattern,
                    Results = results,
                    PassedCount = results.Count(result => result.MatchRank.HasValue),
                    EvaluatedCount = results.Count,
                    TodayGuessDay = todayGuessDay,
                    TodayNumbers = todayNumbers
                });
            }

            return Ok(output.OrderByDescending(row => row.EvaluatedCount == 0 ? 0 : (double)row.PassedCount / row.EvaluatedCount)
                .ThenBy(row => row.Results.Where(result => result.MatchRank.HasValue).Select(result => result.MatchRank!.Value).DefaultIfEmpty(int.MaxValue).Average())
                .ThenBy(row => row.Pattern.ToString(), StringComparer.Ordinal));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(exception.ParamName ?? "request", exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidDataException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (IOException exception)
        {
            ModelState.AddModelError(nameof(request.FileName), $"The selected game file could not be read: {exception.Message}");
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("analyze-pattern-wise-double")]
    [ProducesResponseType<IReadOnlyList<PatternWiseAnalysisRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PatternWiseAnalysisRow>>> AnalyzePatternWiseDouble(
        [FromBody] PatternWiseAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.LatestCount is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(request.LatestCount), "Latest must be between 1 and 4.");
            if (request.SkipLastNumbers is < 0 or > 4)
                throw new ArgumentOutOfRangeException(nameof(request.SkipLastNumbers), "Skip Last Number must be between 0 and 4.");
            if (request.TopCount is < 1 or > 10)
                throw new ArgumentOutOfRangeException(nameof(request.TopCount), "Top count must be between 1 and 10.");
            if (request.DayCount is < 1 or > 30)
                throw new ArgumentOutOfRangeException(nameof(request.DayCount), "Day count must be between 1 and 30.");

            var patterns = request.Patterns.Distinct().ToArray();
            if (patterns.Length == 0)
                throw new ArgumentException("Select at least one panel pattern.", nameof(request.Patterns));

            var fileName = await _panelGameService.ResolveGameFileNameAsync(request.FileName, cancellationToken);
            await using var workbookStream = await _fileStorage.OpenExcelFileAsync(fileName, cancellationToken);
            var workbook = await _excelReaderService.ReadPanelsAsync(workbookStream, cancellationToken);
            var openRows = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, PanelNumberType.Open, PanelPatternType.Sequence).CurrentData;
            var closeRows = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, PanelNumberType.Close, PanelPatternType.Sequence).CurrentData;
            var jodiRows = openRows.Zip(closeRows)
                .Where(pair => !string.IsNullOrWhiteSpace(pair.First.Number) && pair.First.Number != "*" && !string.IsNullOrWhiteSpace(pair.Second.Number) && pair.Second.Number != "*")
                .ToArray();
            if (jodiRows.Length == 0)
                throw new ArgumentException("The selected game does not contain complete Open and Close pairs.", nameof(request.FileName));

            var availableDays = workbook.AvailableDays.Where(day => !string.IsNullOrWhiteSpace(day)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var latestDayIndex = Array.FindIndex(availableDays, day => string.Equals(day, jodiRows[^1].First.DayOfWeek, StringComparison.OrdinalIgnoreCase));
            var todayGuessDay = availableDays[(latestDayIndex + 1) % availableDays.Length];
            var currentOpenNumbers = string.Join(",", openRows.Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*").TakeLast(request.LatestCount).Select(row => row.Number));
            var currentCloseNumbers = string.Join(",", closeRows.Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*").TakeLast(request.LatestCount).Select(row => row.Number));

            var output = new List<PatternWiseAnalysisRow>(patterns.Length);
            foreach (var pattern in patterns)
            {
                var todayNumbers = GetRankedJodis(
                    _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, currentOpenNumbers, PanelNumberType.Open, pattern).NextNumberCounts,
                    _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, currentCloseNumbers, PanelNumberType.Close, pattern).NextNumberCounts,
                    request.TopCount);
                var results = new List<PatternWiseDayResult>(request.DayCount);
                for (var offset = 1; offset <= request.DayCount && request.SkipLastNumbers + offset <= jodiRows.Length; offset++)
                {
                    var skipCount = request.SkipLastNumbers + offset;
                    var openSeed = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, PanelNumberType.Open, PanelPatternType.Sequence, skipCount).CurrentData;
                    var closeSeed = _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, string.Empty, PanelNumberType.Close, PanelPatternType.Sequence, skipCount).CurrentData;
                    var guessOpenNumbers = string.Join(",", openSeed.Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*").TakeLast(request.LatestCount).Select(row => row.Number));
                    var guessCloseNumbers = string.Join(",", closeSeed.Where(row => !string.IsNullOrWhiteSpace(row.Number) && row.Number != "*").TakeLast(request.LatestCount).Select(row => row.Number));
                    var guesses = GetRankedJodis(
                        _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, guessOpenNumbers, PanelNumberType.Open, pattern, skipCount).NextNumberCounts,
                        _panelAnalysisService.Analyze(workbook.Panels, workbook.AvailableDays, guessCloseNumbers, PanelNumberType.Close, pattern, skipCount).NextNumberCounts,
                        request.TopCount);
                    var pass = jodiRows[^skipCount];
                    var passNumber = pass.First.Number + pass.Second.Number;
                    var rank = Array.FindIndex(guesses, value => value == passNumber);
                    results.Add(new PatternWiseDayResult { DayGuess = pass.First.DayOfWeek, Numbers = guesses, PassNumber = passNumber, MatchRank = rank >= 0 ? rank + 1 : null });
                }

                output.Add(new PatternWiseAnalysisRow { Pattern = pattern, Results = results, PassedCount = results.Count(result => result.MatchRank.HasValue), EvaluatedCount = results.Count, TodayGuessDay = todayGuessDay, TodayNumbers = todayNumbers });
            }

            return Ok(output.OrderByDescending(row => row.EvaluatedCount == 0 ? 0 : (double)row.PassedCount / row.EvaluatedCount)
                .ThenBy(row => row.Results.Where(result => result.MatchRank.HasValue).Select(result => result.MatchRank!.Value).DefaultIfEmpty(int.MaxValue).Average())
                .ThenBy(row => row.Pattern.ToString(), StringComparer.Ordinal));
        }
        catch (ArgumentException exception) { ModelState.AddModelError(exception.ParamName ?? "request", exception.Message); return ValidationProblem(ModelState); }
        catch (InvalidDataException exception) { ModelState.AddModelError(nameof(request.FileName), exception.Message); return ValidationProblem(ModelState); }
        catch (IOException exception) { ModelState.AddModelError(nameof(request.FileName), $"The selected game file could not be read: {exception.Message}"); return ValidationProblem(ModelState); }
    }

    private static string[] GetRankedJodis(IReadOnlyList<NextNumberCount> openCounts, IReadOnlyList<NextNumberCount> closeCounts, int topCount) =>
        openCounts.OrderByDescending(item => item.Count).ThenBy(item => item.Number, StringComparer.Ordinal)
            .Zip(closeCounts.OrderByDescending(item => item.Count).ThenBy(item => item.Number, StringComparer.Ordinal))
            .Take(topCount)
            .Select(pair => pair.First.Number + pair.Second.Number)
            .ToArray();
}
