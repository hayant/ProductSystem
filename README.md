# Product management system — smoke test

A minimal vertical slice demonstrating the four-part architecture:

- **Frontend** (React + Vite) — `src/frontend`
- **Backend API** (ASP.NET Core minimal API) — `src/ProductSystem.Api`
- **Database** (EF Core in-memory) — configured in both host projects
- **ERP sync worker** (.NET Worker) — `src/ProductSystem.Worker`
- **Shared domain** (`ProductService`, `Product`, `DbContext`) — `src/ProductSystem.Shared`

## The architectural point this code is making

Both the API and the Worker depend on `ProductSystem.Shared` and go through
`ProductService` for every write. There is no second path to the database.
That single rule is what keeps validation, uniqueness checks, and invariants
in one place rather than duplicated across two entry points.

The ERP is only reachable through `IErpClient`. Anywhere else in the code
calling the ERP directly would be a review-flagging smell — the abstraction
is the translation boundary where the ERP's data shape becomes our domain shape.

## Running it

Prerequisites: .NET 8 SDK, Node 18+.

```bash
# 1. Start the API (terminal 1)
cd src/ProductSystem.Api
dotnet run
# -> http://localhost:5080

# 2. Start the frontend (terminal 2)
cd src/frontend
npm install
npm run dev
# -> http://localhost:5173

# 3. Run the worker to sync a few products from the fake ERP (terminal 3)
#    Note: because the DB is in-memory and per-process, running the worker
#    as a separate process gives it its OWN database. For the smoke test,
#    create products via the UI to see them there; run the worker to see
#    its sync output in the console.
cd src/ProductSystem.Worker
dotnet run
```

## What's deliberately missing (and how it would be added)

- **Update/delete endpoints** — would go in `ProductService` alongside `CreateAsync`,
  with optimistic concurrency already wired via `RowVersion`.
- **Outbound sync (local → ERP)** — mirror of `RunInboundAsync`, querying
  products where `UpdatedAt > outboundWatermark` and calling a
  `PushProductAsync` method on `IErpClient`.
- **Persistent watermark** — a `SyncState` table instead of a hard-coded
  `DateTime.UtcNow.AddDays(-7)`.
- **Real database** — swap `UseInMemoryDatabase` for `UseSqlServer` with a
  connection string from config. The row version already behaves correctly
  against SQL Server.
- **Scheduler** — replace the run-once-and-exit worker with either a
  `BackgroundService` using `PeriodicTimer`, or an external scheduler
  (Kubernetes CronJob, Quartz.NET) triggering the binary.
- **Sync run audit table** — `sync_runs` with start/end/counts for
  observability and alerting on missed or degraded runs.
- **Tests** — unit tests around `ProductService` (easy, since it takes a
  `DbContext` that can be an in-memory one) and contract tests against a
  fake or sandbox ERP.

## Project layout

```
ProductSystem.sln
src/
├── ProductSystem.Shared/      domain + DbContext + ProductService
│   ├── Domain/Product.cs
│   ├── Data/ProductDbContext.cs
│   └── Services/ProductService.cs
├── ProductSystem.Api/         HTTP layer only — no business logic
│   └── Program.cs
├── ProductSystem.Worker/      ERP integration
│   ├── Erp/IErpClient.cs      (+ FakeErpClient)
│   ├── Sync/SyncService.cs
│   └── Program.cs
└── frontend/                  React + Vite
    └── src/
        ├── api.ts             isolated HTTP client
        └── App.tsx            list + create form
```
