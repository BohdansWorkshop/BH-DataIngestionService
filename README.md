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
POST /ingest/generate-load (fullfills database with data)
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

- The Application layer directly references the Infrastructure layer to use the concrete EF Core DbContext. This simplifies the architecture and avoids introducing repository abstractions that would add unnecessary complexity for this scope.
- The database is initialized using EnsureCreatedAsync to keep the setup simple for the assignment. In a production system, schema management would be handled via EF Core migrations.
- Batch-level duplicate detection is performed using in-memory HashSet tracking of normalized keys. This avoids per-row database queries while keeping the implementation efficient and readable.
- In rare cases of concurrent unique constraint violations during batch persistence, the service falls back to per-row inserts for the affected batch segment to ensure accurate error reporting at row level.

## Future Improvements

- Replace `EnsureCreatedAsync` with migrations and deployment-controlled schema updates.
- Add integration tests against real PostgreSQL/Testcontainers.
- Add request size configuration and operational limits based on real CSV sizes.
- Add structured logging around batch import results and timings.
- Consider PostgreSQL `COPY` or provider-specific bulk insert if throughput requirements grow.

## AI Usage

- AI tools were used to accelerate the refactoring and implementation process.
- The accepted output included a simplified project structure, service-oriented ingestion flow, validation layer, EF Core configuration, Docker setup, and focused unit tests.
- AI-generated project complexity was intentionally reduced. This included MediatR, CQRS folder structure, identity scaffolding, Aspire setup, endpoint scanning, repository/unit-of-work abstractions, domain events, etc.

Several issues introduced by AI suggestions were identified and corrected during implementation:

- Incorrect test design for duplicate transaction validation using EF InMemory provider: the AI-suggested test assumed relational database behavior (unique constraint enforcement and DbUpdateException on duplicates). This was not valid in the chosen test setup, since the InMemory provider does not enforce database constraints. The test was therefore replaced with a deterministic unit test focusing on batch-level deduplication logic.
- Avoidance of SaveChanges per CSV row in the normal ingestion path to prevent performance degradation
- Clarification of duplicate handling strategy: enforced both at application level (batch deduplication) and at database level (unique index constraint)

## The following tools and libraries were used in the implementation:

- FluentValidation — for structured and centralized validation of transaction input models, ensuring consistent business rules across single and batch ingestion flows.
- Bogus — for generating realistic synthetic transaction data used in the load generation endpoint and testing scenarios.