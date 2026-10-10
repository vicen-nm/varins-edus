using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Exceptions;
using Xunit;

namespace VarinsEdu.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<(HttpContext Context, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);

        return (context, body);
    }

    [Fact]
    public async Task Tenant_violation_becomes_403_without_leaking_details()
    {
        var (context, body) = await HandleAsync(
            new TenantViolationException("Group: cannot write data of another institution."));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(403, body.GetProperty("status").GetInt32());
        Assert.Equal("tenant_violation", body.GetProperty("code").GetString());
        Assert.DoesNotContain("Group:", body.GetRawText());
    }

    [Fact]
    public async Task Unexpected_error_becomes_500_without_internal_details()
    {
        var (context, body) = await HandleAsync(
            new InvalidOperationException("the database password is hunter2"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.DoesNotContain("hunter2", body.GetRawText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Error_responses_use_problem_json_and_carry_a_trace_id(bool tenantViolation)
    {
        Exception exception = tenantViolation
            ? new TenantViolationException("x")
            : new InvalidOperationException("x");

        var (context, body) = await HandleAsync(exception);

        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }
}
