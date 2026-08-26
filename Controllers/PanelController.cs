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
                SourceUrl = source?.Url ?? game.SourceUrl
            };
        }).ToArray());
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
}
