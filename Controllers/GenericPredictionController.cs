using Microsoft.AspNetCore.Mvc;
using PPG.GuessAPI.Models;

namespace PPG.GuessAPI.Controllers;

[ApiController]
[Route("api/generic-prediction")]
public sealed class GenericPredictionController : ControllerBase
{
    private readonly IGenericPredictionService _predictionService;

    public GenericPredictionController(IGenericPredictionService predictionService)
    {
        _predictionService = predictionService;
    }

    [HttpPost]
    [ProducesResponseType<GenericPredictionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<GenericPredictionResult> Predict([FromBody] GenericPredictionRequest request)
    {
        try
        {
            return Ok(_predictionService.Predict(request.SeriesData, request.Period, request.BacktestSize));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(exception.ParamName ?? nameof(request.SeriesData), exception.Message);
            return ValidationProblem(ModelState);
        }
    }
}
