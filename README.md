# Product management system

A small product-management system split across four parts — three in this
repository, one in a sister repository:

- **Frontend** (React + TypeScript, Vite) — `src/frontend`
- **Backend API** (ASP.NET Core minimal API, `net10.0`) — `src/ProductSystem.Api`
- **Shared domain** (`Product`, `ProductDbContext`, `ProductService`; EF Core 8
  against SQL Server, `net8.0`) — `src/ProductSystem.Shared`
- **ERP sync worker** — `ProductSystem.Integration`, a **separate repository**
  (`../ProductSystem.Integration`). It does *not* reference Shared; it consumes
  this API over HTTP via its `ProductsApiClient`, and reaches the ERP only
  through its `IErpClient` abstraction.

## The architectural point this code is making

Every product write goes through `ProductService` in `ProductSystem.Shared`.
The API calls it directly; the Integration worker goes through the API over
HTTP, so it ends up in the same place. There is deliberately **no second path
to the database** — validation, SKU uniqueness, and other invariants live in
exactly one place rather than duplicated across entry points. Adding
update/delete endpoints means adding methods to `ProductService` first, then
thin HTTP mappings in `Program.cs`.

In the Integration repo, the ERP is only reachable through `IErpClient`.
Anywhere else calling the ERP directly would be a review-flagging smell — the
abstraction is the translation boundary where the ERP's data shape becomes our
domain shape.

## Running it

Prerequisites: .NET 10 SDK (the API targets `net10.0`; Shared targets
`net8.0`), Node 18+, and a reachable SQL Server instance.

### 1. Configure the database connection

The API **fails at startup** if `ConnectionStrings:ProductSystem` is not
configured — fail loudly rather than discover it on the first request. Set it
via User Secrets locally:

```bash
cd src/ProductSystem.Api
dotnet user-secrets set "ConnectionStrings:ProductSystem" \
  "Server=localhost;Database=ProductSystem;Integrated Security=true;TrustServerCertificate=True"
```

(or the `ConnectionStrings__ProductSystem` environment variable). Pending EF
migrations are applied automatically on startup.

### 2. Start the API

```bash
dotnet run --project src/ProductSystem.Api
# -> http://localhost:5080
```

### 3. Start the frontend

```bash
cd src/frontend
npm install
npm run dev
# -> http://localhost:5173
```

`.env.development` provides `VITE_API_BASE` and `VITE_API_KEY` for local dev.
These are baked into the bundle at build time and are **public configuration,
not secrets** (the `X-Api-Key` value is visible in DevTools). Local overrides
go in `.env.local` (gitignored).

### 4. Run the ERP sync (optional)

Clone and run `ProductSystem.Integration` (sister repo); point its
`ProductsApiClient` configuration at this API.

## API surface

Endpoints live under the versioned prefix `/api/v1`:

- `GET /api/v1/products` — list products
- `POST /api/v1/products` — create a product
- `GET /health` — health check (includes a database ping), unauthenticated

### Authentication

Every request (except `/health`) must carry the correct `X-Api-Key` header,
enforced by `ApiKeyMiddleware` (constant-time comparison; returns 500 if the
key was never configured — fail loudly rather than silently open). The dev key
(`dev-smoke-test-key`) is committed in `appsettings.Development.json` and
`frontend/.env.development` on purpose.

Two pipeline-ordering rules in `Program.cs`:

- `UseCors()` runs **before** the API key middleware so browser preflight
  (OPTIONS) requests are answered.
- `/health` is mapped before, and also explicitly skipped inside, the
  middleware: App Service probes it unauthenticated, and a 401 would mark the
  instance unhealthy.

CORS origins and the API key come from config (`Cors:AllowedOrigins`,
`ApiAuth:ApiKey`) so the binary is environment-independent.

### Error contract

The API returns structured error bodies the frontend renders:

- validation failures → `400 { error: "validation_failed", message }`
- SKU conflicts → `409 { error: "duplicate_sku", message, sku }`

`src/frontend/src/api.ts` is the single place HTTP concerns live on the
client — components only see its clean async functions.

## Domain conventions

- `Product` is created via the static `Product.Create` factory (private
  setters, parameterless ctor only for EF); validation lives in the entity.
- Optimistic concurrency via `RowVersion` (`IsRowVersion()` — real against
  SQL Server).
- `Sku` has a unique index; `ProductService.CreateAsync` also pre-checks and
  throws `DuplicateSkuException` for a clean error (the index is the last line
  of defence).
- `ExternalId` is the ERP's identifier for a product, nullable so
  locally-created products can exist before outbound sync.
- DTOs in `Program.cs` are separate records — never serialize the domain
  entity over HTTP.

## Migrations

Shared owns the model; Api is the startup project:

```bash
ConnectionStrings__ProductSystem="Server=localhost;Database=DesignTime;Integrated Security=true;TrustServerCertificate=True" \
  dotnet ef migrations add <Name> -p src/ProductSystem.Shared -s src/ProductSystem.Api -o Migrations
```

## What's deliberately missing (and how it would be added)

- **Update/delete endpoints** — would go in `ProductService` alongside
  `CreateAsync`, with optimistic concurrency already wired via `RowVersion`.
- **Tests** — unit tests around `ProductService` (easy, since it takes a
  `DbContext` that can be an in-memory one) and, in the Integration repo,
  contract tests against a fake or sandbox ERP.

## Project layout

```
ProductSystem.sln
src/
├── ProductSystem.Shared/      domain + DbContext + ProductService (net8.0)
│   ├── Domain/Product.cs
│   ├── Data/ProductDbContext.cs
│   ├── Services/ProductService.cs
│   └── Migrations/
├── ProductSystem.Api/         HTTP layer only — no business logic (net10.0)
│   ├── Middleware/ApiKeyMiddleware.cs
│   └── Program.cs
└── frontend/                  React + Vite
    └── src/
        ├── api.ts             isolated HTTP client
        └── App.tsx            list + create form

../ProductSystem.Integration/  ERP sync worker (separate repository)
```
