using Microsoft.EntityFrameworkCore;
using ProductSystem.Api.Endpoints;
using ProductSystem.Api.Middleware;
using ProductSystem.Shared.Data;
using ProductSystem.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from config — appsettings, User Secrets (locally),
// or env vars (App Service sets ConnectionStrings__ProductSystem). We fail loudly
// at startup if it's missing rather than discovering it on the first request.
var connectionString = builder.Configuration.GetConnectionString("ProductSystem")
    ?? throw new InvalidOperationException("ConnectionStrings:ProductSystem is not configured.");

builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ProductService>();

// Translates domain exceptions into the API's structured error contract, in one place.
builder.Services.AddExceptionHandler<ProductExceptionHandler>();
builder.Services.AddProblemDetails();

// Health checks expose a /health endpoint App Service can probe. The DbContext
// check pings the database, so the endpoint goes Unhealthy if the DB is down.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ProductDbContext>("database");

// CORS origins are environment-specific — localhost in dev, the production
// frontend URL in App Service. Read from config so the binary doesn't change.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Apply pending migrations on startup. Fine for a smoke-test environment;
// a production setup would run migrations as a separate CI step to avoid
// race conditions when multiple instances start at once.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    db.Database.Migrate();
}

// Catches exceptions from downstream middleware/endpoints and runs the registered
// IExceptionHandler(s) — translating domain exceptions into the API error contract.
app.UseExceptionHandler();

// UseCors must come first so that browser preflight (OPTIONS) requests
// are answered before the API key middleware runs — otherwise the browser
// never sees the CORS headers and blocks the preflight.
app.UseCors();

// Health endpoint is intentionally unauthenticated so App Service can probe it.
// The ApiKeyMiddleware also short-circuits on /health for defence in depth.
app.MapHealthChecks("/health");

app.UseMiddleware<ApiKeyMiddleware>();

// Versioned API prefix — baked in from day one, as discussed in the design.
// Each feature group owns its mappings in Endpoints/; Program.cs just composes them.
var api = app.MapGroup("/api/v1");
api.MapProductEndpoints();

app.Run();
