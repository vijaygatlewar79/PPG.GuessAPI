using Microsoft.AspNetCore.Mvc;
using PPG.GuessData;
using PPG.GuessData.Models;

namespace PPG.GuessAPI.Controllers;

[ApiController]
[Route("api/chart-export")]
public sealed class ChartExportController : ControllerBase
{
    private readonly IChartExcelService _chartExcelService;
    private readonly ChartSourceCatalog _chartSourceCatalog;
    private readonly IPanelFileStorage _fileStorage;

    public ChartExportController(
        IChartExcelService chartExcelService,
        ChartSourceCatalog chartSourceCatalog,
        IPanelFileStorage fileStorage)
    {
        _chartExcelService = chartExcelService;
        _chartSourceCatalog = chartSourceCatalog;
        _fileStorage = fileStorage;
    }

    [HttpGet("options")]
    [ProducesResponseType<ChartExcelOptions>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChartExcelOptions>> GetOptions(CancellationToken cancellationToken)
    {
        return Ok(await _chartSourceCatalog.GetOptionsAsync(cancellationToken));
    }

    [HttpPost("open")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> OpenFile(
        [FromBody] OpenChartFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileName = await _chartSourceCatalog.GetFileNameAsync(
                request.FileName,
                cancellationToken);

            if (fileName is null
                || !await _fileStorage.ExcelFileExistsAsync(fileName, cancellationToken))
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The configured Excel file was not found.");
            }

            var stream = await _fileStorage.OpenExcelFileAsync(fileName, cancellationToken);
            return File(
                stream,
                GetExcelContentType(fileName),
                fileName,
                enableRangeProcessing: true);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(request), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (FileNotFoundException)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The configured Excel file was not found.");
        }
        catch (IOException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The Excel file could not be downloaded.",
                detail: exception.Message);
        }
    }

    [HttpDelete("options")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteOption(
        [FromQuery] string fileName,
        [FromQuery] ExcelFileBackupAction? backupAction,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await _chartSourceCatalog.DeleteAsync(
                fileName,
                backupAction ?? ExcelFileBackupAction.Remove,
                cancellationToken);

            if (!removed)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The configured Excel file was not found.");
            }

            return NoContent();
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(fileName), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (IOException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The configured Excel file could not be removed.",
                detail: exception.Message);
        }
    }

    [HttpPost("generate")]
    [ProducesResponseType<ChartExcelResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChartExcelResult>> Generate(
        [FromBody] ChartExcelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.DisplayName is not null && string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new ArgumentException("Display name is required when provided.", nameof(request.DisplayName));
            }

            var result = await _chartExcelService.GenerateExcelAsync(
                request,
                cancellationToken);

            await _chartSourceCatalog.SaveAsync(
                result.FileName,
                request.DisplayName,
                request.Url,
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(request), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The chart page could not be converted.",
                detail: exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The chart page could not be downloaded.",
                detail: exception.Message);
        }
        catch (IOException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The Excel file could not be saved.",
                detail: exception.Message);
        }
    }

    private static string GetExcelContentType(string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.ms-excel.sheet.macroEnabled.12"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
