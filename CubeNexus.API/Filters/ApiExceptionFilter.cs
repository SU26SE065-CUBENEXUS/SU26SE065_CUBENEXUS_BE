using System.Net.Http;
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

        if (context.Exception is HttpRequestException httpEx)
        {
            logger.LogError(httpEx, "Upstream HTTP failure for {Path}", context.HttpContext.Request.Path);
            var status = (int?)httpEx.StatusCode is >= 400 and < 600
                ? (int)httpEx.StatusCode!
                : StatusCodes.Status502BadGateway;
            context.Result = new ObjectResult(new
            {
                code = "FACE_AI_UPSTREAM_ERROR",
                message = Truncate(httpEx.Message, 400),
            })
            {
                StatusCode = status
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

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "...";
    }
}
