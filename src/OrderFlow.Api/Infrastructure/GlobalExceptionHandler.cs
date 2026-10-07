using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Common;

namespace OrderFlow.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 problem details. Unexpected errors never leak their message.</summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Extensions =
                {
                    ["errors"] = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
                }
            },
            ConflictException conflict => Create(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            NotFoundException notFound => Create(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            UnauthorizedException unauthorized => Create(StatusCodes.Status401Unauthorized, "Unauthorized", unauthorized.Message),
            DomainException domain => Create(StatusCodes.Status422UnprocessableEntity, "Business rule violated", domain.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => Create(
                StatusCodes.Status409Conflict,
                "Conflict",
                "The request conflicts with a concurrent change. Please retry."),
            DbUpdateConcurrencyException => Create(
                StatusCodes.Status409Conflict,
                "Concurrent update",
                "The data was changed by another request. Please retry."),
            _ => null
        };

        if (problem is null)
        {
            LogUnexpected(exception, httpContext.Request.Method, httpContext.Request.Path);
            problem = Create(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.");
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Create(int status, string title, string detail) => new() { Status = status, Title = title, Detail = detail };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnexpected(Exception exception, string method, PathString path);
}
