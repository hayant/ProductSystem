# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Issue / PR workflow

When asked to work on a GitHub issue, follow this flow:

1. Create a feature branch off `main` (e.g. `issue-<number>-short-description`). **Never commit
   directly to `main`.**
2. Implement the change and verify it (build, tests, and browser verification for UI changes).
3. Show the changes to the user and **wait for their review before committing**.
4. After approval: commit, push the branch, and open a PR that references the issue
   (`Fixes #<number>` in the PR body so the merge auto-closes it).
5. Let the user merge the PR (or merge only when they explicitly ask), and confirm the issue
   closed afterwards.

## Commands

### Backend (.NET)
```bash
dotnet build                                # build whole solution (from repo root)
dotnet run --project src/ProductSystem.Api  # http://localhost:5080
```

The API **fails at startup** if `ConnectionStrings:ProductSystem` is not configured — set it via
User Secrets locally or the `ConnectionStrings__ProductSystem` env var. It expects SQL Server and
applies pending EF migrations automatically on startup.

```bash
# Add a migration (Shared owns the model; Api is the startup project)
ConnectionStrings__ProductSystem="Server=localhost;Database=DesignTime;Integrated Security=true;TrustServerCertificate=True" \
  dotnet ef migrations add <Name> -p src/ProductSystem.Shared -s src/ProductSystem.Api -o Migrations
```

There are no tests yet.

### Frontend (React + TypeScript, Vite)
Run from `src/frontend`:
```bash
npm install
npm run dev              # http://localhost:5173
npm run build            # tsc + vite build
```

`.env.development` provides `VITE_API_BASE` and `VITE_API_KEY` for local dev. These are baked into
the bundle at build time and are **public configuration, not secrets** (the `X-Api-Key` value is
visible in DevTools). Local overrides go in `.env.local` (gitignored).

## Architecture

### Three pieces here, a fourth in a sister repository
- **ProductSystem.Shared** (`net8.0`) — domain (`Product`), `ProductDbContext`, and
  `ProductService`. EF Core 8 against SQL Server.
- **ProductSystem.Api** (`net10.0`) — ASP.NET Core minimal API, HTTP layer only, no business
  logic. Endpoints live under the versioned prefix `/api/v1`.
- **frontend** — React + Vite SPA; talks to the API over HTTP.
- **ProductSystem.Integration** (separate repo, `../ProductSystem.Integration`) — the ERP sync
  worker. It does **not** reference Shared; it consumes this API over HTTP via its
  `ProductsApiClient`, and reaches the ERP only through its `IErpClient` abstraction.

Note: `README.md` predates this split — it still describes an in-process Worker and an in-memory
database. Trust the code (and this file) over the README on those points.

### Every write goes through ProductService
Both the API and the Integration worker funnel all product writes through
`ProductSystem.Shared/Services/ProductService.cs` (the worker indirectly, via the API). There is
deliberately **no second path to the database** — validation, SKU uniqueness, and other invariants
live in exactly one place. Adding update/delete endpoints means adding methods to `ProductService`
first, then thin HTTP mappings in `Program.cs`.

### API key auth, CORS, and health checks interact
Every request must carry the correct `X-Api-Key` header, enforced by `ApiKeyMiddleware`
(constant-time comparison; returns 500 if the key was never configured — fail loudly rather than
silently open). Two pipeline-ordering rules to preserve in `Program.cs`:
- `UseCors()` runs **before** the API key middleware so browser preflight (OPTIONS) requests are
  answered — otherwise the browser blocks every cross-origin call.
- `/health` is mapped before, and also explicitly skipped inside, the middleware: App Service
  probes it unauthenticated, and a 401 would mark the instance unhealthy.

CORS origins and the API key come from config (`Cors:AllowedOrigins`, `ApiAuth:ApiKey`) so the
binary is environment-independent. The dev key (`dev-smoke-test-key`) is committed in
`appsettings.Development.json` and `frontend/.env.development` on purpose.

### Domain conventions
- `Product` is created via the static `Product.Create` factory (private setters, parameterless
  ctor only for EF); validation lives in the entity.
- Optimistic concurrency via `RowVersion` (`IsRowVersion()` — real against SQL Server).
- `Sku` has a unique index; `ProductService.CreateAsync` also pre-checks and throws
  `DuplicateSkuException` for a clean error (the index is the last line of defence).
- `ExternalId` is the ERP's identifier for a product, nullable so locally-created products can
  exist before outbound sync.
- DTOs in `Program.cs` are separate records — never serialize the domain entity over HTTP.

### Error contract
The API returns structured error bodies the frontend renders: validation failures →
`400 { error: "validation_failed", message }`, SKU conflicts →
`409 { error: "duplicate_sku", message, sku }`. Keep new endpoints consistent with this shape.
`src/frontend/src/api.ts` is the single place HTTP concerns live on the client — components only
see its clean async functions.
