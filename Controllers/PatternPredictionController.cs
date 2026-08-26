using Google.GenAI;
using Microsoft.AspNetCore.Mvc;
using PPG.GuessAPI.Models;
using System.Globalization;
using System.Text.Json;

namespace PPG.GuessAPI.Controllers;

[ApiController]
[Route("api/pattern-prediction")]
public sealed class PatternPredictionController : ControllerBase
{
    private static readonly JsonSerializerOptions PredictionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPatternPredictionService _predictionService;
    private readonly ILogger<PatternPredictionController> _logger;

    public PatternPredictionController(
        IPatternPredictionService predictionService,
        ILogger<PatternPredictionController> logger)
    {
        _predictionService = predictionService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType<PatternPredictionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PatternPredictionResult>> Predict(
        [FromBody] PatternPredictionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SeriesData))
        {
            ModelState.AddModelError(nameof(request.SeriesData), "Series data is required.");
            return ValidationProblem(ModelState);
        }

        if (!Enum.TryParse<PatternPredictionMode>(
                request.PredictionMode,
                ignoreCase: true,
                out var predictionMode)
            || !Enum.IsDefined(predictionMode))
        {
            ModelState.AddModelError(
                nameof(request.PredictionMode),
                "Prediction mode must be Standard or Deep.");
            return ValidationProblem(ModelState);
        }

        if (!_predictionService.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Gemini prediction is not configured.",
                detail: "Set GEMINI_API_KEY or Gemini__ApiKey, then restart the API.");
        }

        try
        {
            var predictionJson = await _predictionService.PredictAsync(
                request.SeriesData,
                predictionMode,
                cancellationToken);
            var prediction = ParsePrediction(predictionJson);

            return Ok(new PatternPredictionResult
            {
                Prediction = prediction.Explanation,
                PredictedNumber = prediction.PredictedNumbers[0].ToString(CultureInfo.InvariantCulture),
                PredictedNumbers = prediction.PredictedNumbers
                    .Select(number => number.ToString(CultureInfo.InvariantCulture))
                    .ToArray(),
                Model = _predictionService.Model
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "Gemini returned an invalid pattern prediction.");
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Gemini returned an invalid prediction.",
                detail: exception.Message);
        }
        catch (ClientError exception) when (IsQuotaExceeded(exception))
        {
            _logger.LogWarning(exception, "Gemini request quota was exceeded.");
            return Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Gemini request quota exceeded.",
                detail: "The configured Gemini model has reached its current request limit. Try again after the quota resets or enable Gemini API billing.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Gemini pattern prediction failed.");
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Gemini could not generate the prediction.",
                detail: "Check the Gemini API key, model access, and network connection.");
        }
    }

    private static bool IsQuotaExceeded(ClientError exception) =>
        exception.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static ParsedGeminiPrediction ParsePrediction(string predictionJson)
    {
        GeminiPredictionPayload? prediction;
        try
        {
            prediction = JsonSerializer.Deserialize<GeminiPredictionPayload>(
                predictionJson,
                PredictionJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Gemini returned malformed structured prediction data.",
                exception);
        }

        if (prediction is null || string.IsNullOrWhiteSpace(prediction.Explanation))
        {
            throw new InvalidOperationException("Gemini returned an empty prediction explanation.");
        }

        if (prediction.PredictedNumbers is not { Length: 3 } predictedNumbers
            || predictedNumbers.Any(number => number is < 0 or > 9)
            || predictedNumbers.Distinct().Count() != 3)
        {
            throw new InvalidOperationException("Gemini did not return three unique predicted digits from 0 through 9.");
        }

        return new ParsedGeminiPrediction(prediction.Explanation.Trim(), predictedNumbers);
    }

    private sealed class GeminiPredictionPayload
    {
        public string Explanation { get; init; } = string.Empty;

        public int[]? PredictedNumbers { get; init; }
    }

    private sealed record ParsedGeminiPrediction(string Explanation, int[] PredictedNumbers);
}
