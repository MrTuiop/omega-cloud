using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Api.Middleware;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Domain.Exceptions;

namespace OmegaCloud.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenDomainExceptionThrown_ShouldReturnBadRequest()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DomainException("Domain rule violated: file quota exceeded"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        Assert.Contains("Domain rule violated: file quota exceeded", responseText);
    }

    [Fact]
    public async Task InvokeAsync_WhenValidationExceptionThrown_ShouldReturnBadRequestWithErrors()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new ValidationException("Validation failed", new Dictionary<string, string[]>
            {
                { "Username", new[] { "Username is required" } }
            }),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        Assert.Contains("Validation failed", responseText);
        Assert.Contains("Username is required", responseText);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedExceptionThrown_ShouldReturnUnauthorized()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new UnauthorizedException("Invalid token"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenGenericExceptionThrown_ShouldReturnInternalServerError()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Fatal database corruption"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseText = await reader.ReadToEndAsync();
        Assert.Contains("An unexpected internal error occurred.", responseText);
        // Ensure sensitive internal exception message is NOT leaked to clients
        Assert.DoesNotContain("Fatal database corruption", responseText);
    }
}
