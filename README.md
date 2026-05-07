# BH Data Ingestion Service

.NET 8 Web API for ingesting customer transaction data through single JSON requests and streamed CSV uploads.

## Run

```bash
docker compose up --build
```

The API listens on `http://localhost:8080`.

For local development without Docker:

```bash
dotnet restore
dotnet run --project src/Web/Web.csproj
```

Set `ConnectionStrings__DefaultConnection` if PostgreSQL is not running with the default settings from `docker-compose.yml`.

## Endpoints

```http
POST /ingest/transaction
POST /ingest/batch
GET  /customers/{id}/transactions?page=1&pageSize=50&dateFrom=2024-01-01&dateTo=2024-12-31&currency=USD
GET  /stats/summary
```

CSV uploads expect these headers:

```csv
CustomerId,TransactionDate,Amount,Currency,SourceChannel
C1,2024-01-01T12:00:00Z,10.50,USD,web
```

## Architecture

The original Clean Architecture template was reduced to the pieces this assignment needs:

- `Web`: controllers and global exception middleware.
- `Application`: DTOs, validation, ingestion/query/stat services.
- `Domain`: transaction entity.
- `Infrastructure`: EF Core `ApplicationDbContext` and PostgreSQL mapping.
- `tests/Application.UnitTests`: focused xUnit tests.

Controllers are intentionally thin. Services own validation, duplicate handling, CSV processing, queries, and aggregation. EF Core is used directly instead of repositories or unit-of-work abstractions.

## Data Rules

Duplicate transactions are defined by:

`CustomerId + TransactionDate + Amount + Currency + SourceChannel`

The service checks duplicates before inserting and the database enforces the same rule with a unique index. Additional indexes are configured for `CustomerId` and `TransactionDate`.

## Batch Ingestion

CSV ingestion uses CsvHelper over the request stream and processes rows incrementally. Valid rows are accumulated in batches of 750 before EF Core writes them. The API returns accepted count, rejected count, and row-level errors.

## Trade-Offs

- `Application` references `Infrastructure` so services can use the concrete EF Core context directly. This is less formally layered, but avoids repository abstractions that add little value here.
- Startup uses `EnsureCreatedAsync` for assignment simplicity. A production service should use migrations.
- Batch duplicate checks query likely candidates once per batch, then compare exact duplicate keys in memory. This keeps the implementation readable while avoiding a database query per row.
- On rare concurrent unique conflicts during batch save, the service falls back to per-row inserts for that failed batch to report useful row errors.

## Future Improvements

- Replace `EnsureCreatedAsync` with migrations and deployment-controlled schema updates.
- Add integration tests against real PostgreSQL/Testcontainers.
- Add request size configuration and operational limits based on real CSV sizes.
- Add structured logging around batch import results and timings.
- Consider PostgreSQL `COPY` or provider-specific bulk insert if throughput requirements grow.

## AI Usage

AI tools were used to accelerate the refactor and implementation. The accepted output was the simplified project structure, service-oriented ingestion flow, validation, EF Core configuration, Docker setup, and focused tests.

AI-generated template-style complexity was intentionally removed or modified: MediatR, CQRS folders, identity, Aspire, endpoint scanning, repositories, unit-of-work patterns, domain events, and generated Todo code were discarded.

Mistakes caught and corrected during the work included keeping package versions aligned to .NET 8 instead of the generated .NET 10 template, avoiding SaveChanges per CSV row in the normal path, and keeping duplicate detection enforced both in application logic and at the database index level.
