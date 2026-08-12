using CubeNexus.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CubeNexus.API.Filters;

public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is CustomException custom)
        {
            context.Result = new ObjectResult(new { code = custom.ErrorCode, message = custom.Message })
            {
                StatusCode = custom.StatusCode
            };
            context.ExceptionHandled = true;
            return;
        }

        logger.LogError(context.Exception, "Unhandled API exception for {Path}", context.HttpContext.Request.Path);
        context.Result = new ObjectResult(new { code = "INTERNAL_ERROR", message = "An unexpected server error occurred." })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
        context.ExceptionHandled = true;
    }
}
