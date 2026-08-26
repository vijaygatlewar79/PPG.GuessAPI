using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PPG.GuessAPI;

public sealed class AzureStorageExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var isConcurrencyConflict = context.Exception is RequestFailedException
        {
            Status: StatusCodes.Status409Conflict or StatusCodes.Status412PreconditionFailed
        };
        if (!isConcurrencyConflict
            && context.Exception is not RequestFailedException
            && context.Exception is not AuthenticationFailedException)
        {
            return;
        }

        var statusCode = isConcurrencyConflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status503ServiceUnavailable;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = isConcurrencyConflict
                ? "The Azure Storage data changed during this request."
                : "Azure Blob Storage is unavailable.",
            Detail = isConcurrencyConflict
                ? "Retry the request with the latest data."
                : "Verify the Azure Storage configuration, identity, and RBAC role assignment."
        };

        context.Result = new ObjectResult(problem) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}
