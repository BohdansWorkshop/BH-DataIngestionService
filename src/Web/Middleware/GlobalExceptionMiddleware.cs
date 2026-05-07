using System.Text.Json;
using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Exceptions;

namespace BH_DataIngestionService.Web.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (ValidationException exception)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                new ApiError("VALIDATION_ERROR", exception.Message, exception.Errors));
        }
        catch (DuplicateTransactionException exception)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status409Conflict,
                new ApiError("DUPLICATE_TRANSACTION", exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API exception.");

            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                new ApiError("INTERNAL_ERROR", "An unexpected error occurred."));
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, ApiError error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(context.Response.Body, error, JsonOptions, context.RequestAborted);
    }
}
