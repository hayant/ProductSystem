using System.Security.Cryptography;
using System.Text;

namespace ProductSystem.Api.Middleware;

/// <summary>
/// Smoke-test API key guard. Every request must carry the correct X-Api-Key header.
/// CORS preflight (OPTIONS) is handled by UseCors() before this middleware runs,
/// so browser requests are not blocked by the preflight check.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        // Health probes must work without credentials — App Service load balancers
        // call /health unauthenticated, and a 401 there would mark the instance unhealthy.
        // MapHealthChecks runs before this middleware in the pipeline, but the explicit
        // skip here is belt-and-braces in case the pipeline order ever changes.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var expected = config["ApiAuth:ApiKey"];

        // Fail loudly if the key was never configured — better than silently open.
        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "server_misconfiguration",
                message = "ApiAuth:ApiKey is not set."
            });
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            || string.IsNullOrEmpty(incoming))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "missing_api_key",
                message = $"Request must include the {HeaderName} header."
            });
            return;
        }

        // FixedTimeEquals prevents timing attacks when the key lengths match.
        // If lengths differ it returns false immediately — still safe.
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var incomingBytes = Encoding.UTF8.GetBytes(incoming.ToString());

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, incomingBytes))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "invalid_api_key",
                message = "The provided API key is not valid."
            });
            return;
        }

        await next(context);
    }
}
