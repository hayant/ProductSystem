using Microsoft.AspNetCore.Diagnostics;
using ProductSystem.Shared.Services;

namespace ProductSystem.Api.Endpoints;

// Single place the domain's exceptions become the API's structured error bodies.
// Keeps the error contract in one spot so it can't drift as endpoints are added
// across multiple files (mirrors the single-write-path discipline in ProductService).
//   - ArgumentException     -> 400 { error: "validation_failed", message }
//   - DuplicateSkuException -> 409 { error: "duplicate_sku", message, sku }
public sealed class ProductExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, body) = exception switch
        {
            DuplicateSkuException ex => (
                StatusCodes.Status409Conflict,
                (object)new { error = "duplicate_sku", message = ex.Message, sku = ex.Sku }),
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                new { error = "validation_failed", message = ex.Message }),
            _ => (0, (object?)null)!,
        };

        // Not an exception we own — let the default pipeline handle it.
        if (statusCode == 0)
            return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(body, cancellationToken);
        return true;
    }
}
