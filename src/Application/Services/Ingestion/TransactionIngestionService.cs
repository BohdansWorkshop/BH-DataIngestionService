using System.Globalization;
using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Exceptions;
using BH_DataIngestionService.Application.Validation;
using BH_DataIngestionService.Domain.Entities;
using BH_DataIngestionService.Infrastructure.Data;
using CsvHelper;
using CsvHelper.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AppValidationException = BH_DataIngestionService.Application.Exceptions.ValidationException;

namespace BH_DataIngestionService.Application.Services.Ingestion;

public sealed class TransactionIngestionService(
    ApplicationDbContext dbContext,
    IValidator<TransactionRequest> validator)
{
    private const int BatchSize = 750;
    private const string UniqueViolationSqlState = "23505";

    public async Task<TransactionResponse> IngestAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var normalizedRequest = await ValidateAndNormalizeAsync(request, cancellationToken);

        if (RequiresApplicationDuplicateCheck() &&
            await ExistsAsync(DuplicateTransactionKey.FromRequest(normalizedRequest), cancellationToken))
        {
            throw new DuplicateTransactionException();
        }

        var transaction = CreateTransaction(normalizedRequest);
        dbContext.Transactions.Add(transaction);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new DuplicateTransactionException();
        }

        return ToResponse(transaction);
    }

    public async Task<BatchIngestResponse> IngestBatchAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null
        });

        var batchState = new BatchIngestionState();
        var duplicateKeysSeenInFile = new HashSet<DuplicateTransactionKey>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowNumber = csv.Context.Parser?.Row ?? 0;
            BatchTransactionRow csvRow;

            try
            {
                csvRow = csv.GetRecord<BatchTransactionRow>();
            }
            catch (Exception exception) when (exception is CsvHelperException or FormatException)
            {
                batchState.Errors.Add(new RowError(rowNumber, "CSV_PARSE_ERROR", "CSV row could not be parsed."));
                continue;
            }

            var request = new TransactionRequest(
                csvRow.CustomerId,
                csvRow.TransactionDate,
                csvRow.Amount,
                csvRow.Currency,
                csvRow.SourceChannel);

            await AddValidRowAsync(
                rowNumber,
                request,
                duplicateKeysSeenInFile,
                "Duplicate transaction in uploaded CSV.",
                batchState,
                cancellationToken);
        }

        await FlushBatchAsync(batchState, cancellationToken);

        return batchState.ToResponse();
    }

    public async Task<BatchIngestResponse> IngestBatchAsync(
        IEnumerable<TransactionRequest> requests,
        CancellationToken cancellationToken)
    {
        var rowNumber = 0;
        var batchState = new BatchIngestionState();
        var duplicateKeysSeenInBatch = CreateDuplicateKeySet(requests);

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            await AddValidRowAsync(
                rowNumber,
                request,
                duplicateKeysSeenInBatch,
                "Duplicate transaction in uploaded batch.",
                batchState,
                cancellationToken);
        }

        await FlushBatchAsync(batchState, cancellationToken);

        return batchState.ToResponse();
    }

    private async Task AddValidRowAsync(
        int rowNumber,
        TransactionRequest request,
        HashSet<DuplicateTransactionKey> duplicateKeysSeen,
        string duplicateMessage,
        BatchIngestionState batchState,
        CancellationToken cancellationToken)
    {
        TransactionRequest normalizedRequest;
        try
        {
            normalizedRequest = await ValidateAndNormalizeAsync(request, cancellationToken);
        }
        catch (AppValidationException exception)
        {
            batchState.Errors.Add(new RowError(rowNumber, "VALIDATION_ERROR", "Invalid transaction.", exception.Errors));
            return;
        }

        var duplicateKey = DuplicateTransactionKey.FromRequest(normalizedRequest);
        if (!duplicateKeysSeen.Add(duplicateKey))
        {
            batchState.Errors.Add(new RowError(rowNumber, "DUPLICATE_TRANSACTION", duplicateMessage));
            return;
        }

        batchState.PendingRows.Add(new PendingTransactionRow(rowNumber, normalizedRequest));

        if (batchState.PendingRows.Count >= BatchSize)
        {
            await FlushBatchAsync(batchState, cancellationToken);
        }
    }

    private async Task FlushBatchAsync(BatchIngestionState batchState, CancellationToken cancellationToken)
    {
        if (batchState.PendingRows.Count == 0)
        {
            return;
        }

        batchState.AcceptedCount += await SaveBatchAsync(batchState.PendingRows, batchState.Errors, cancellationToken);
        batchState.PendingRows.Clear();
    }

    private async Task<int> SaveBatchAsync(
        IReadOnlyList<PendingTransactionRow> batchRows,
        ICollection<RowError> errors,
        CancellationToken cancellationToken)
    {
        if (batchRows.Count == 0)
        {
            return 0;
        }

        var originalAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (var index = 0; index < batchRows.Count; index++)
            {
                dbContext.Transactions.Add(CreateTransaction(batchRows[index].Request));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return batchRows.Count;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            dbContext.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetectChanges;
            return await SaveRowsAfterBatchConflictAsync(batchRows, errors, cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetectChanges;
        }
    }

    private async Task<int> SaveRowsAfterBatchConflictAsync(
        IReadOnlyList<PendingTransactionRow> batchRows,
        ICollection<RowError> errors,
        CancellationToken cancellationToken)
    {
        var acceptedCount = 0;

        for (var index = 0; index < batchRows.Count; index++)
        {
            var row = batchRows[index];
            dbContext.Transactions.Add(CreateTransaction(row.Request));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                acceptedCount++;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                errors.Add(new RowError(row.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        return acceptedCount;
    }

    private static HashSet<DuplicateTransactionKey> CreateDuplicateKeySet(IEnumerable<TransactionRequest> requests)
    {
        return requests is ICollection<TransactionRequest> collection
            ? new HashSet<DuplicateTransactionKey>(collection.Count)
            : [];
    }

    private async Task<TransactionRequest> ValidateAndNormalizeAsync(
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new AppValidationException("Invalid transaction.", validationResult.ToErrorDictionary());
        }

        return request with
        {
            CustomerId = request.CustomerId!.Trim(),
            Currency = request.Currency!.Trim().ToUpperInvariant(),
            SourceChannel = request.SourceChannel!.Trim()
        };
    }

    private Task<bool> ExistsAsync(DuplicateTransactionKey duplicateKey, CancellationToken cancellationToken)
    {
        return dbContext.Transactions.AnyAsync(transaction =>
            transaction.CustomerId == duplicateKey.CustomerId &&
            transaction.TransactionDate == duplicateKey.TransactionDate &&
            transaction.Amount == duplicateKey.Amount &&
            transaction.Currency == duplicateKey.Currency &&
            transaction.SourceChannel == duplicateKey.SourceChannel,
            cancellationToken);
    }

    private static Transaction CreateTransaction(TransactionRequest request)
    {
        return new Transaction
        {
            CustomerId = request.CustomerId!,
            TransactionDate = request.TransactionDate,
            Amount = request.Amount,
            Currency = request.Currency!,
            SourceChannel = request.SourceChannel!,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static TransactionResponse ToResponse(Transaction transaction)
    {
        return new TransactionResponse(
            transaction.Id,
            transaction.CustomerId,
            transaction.TransactionDate,
            transaction.Amount,
            transaction.Currency,
            transaction.SourceChannel,
            transaction.CreatedAtUtc);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == UniqueViolationSqlState;
    }

    private bool RequiresApplicationDuplicateCheck()
    {
        return dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
    }

    private sealed class BatchIngestionState
    {
        public int AcceptedCount { get; set; }

        public List<PendingTransactionRow> PendingRows { get; } = new(BatchSize);

        public List<RowError> Errors { get; } = [];

        public BatchIngestResponse ToResponse()
        {
            return new BatchIngestResponse(AcceptedCount, Errors.Count, Errors);
        }
    }
}
