using System.Diagnostics;
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

namespace BH_DataIngestionService.Application.Services;

public sealed class TransactionIngestionService(
    ApplicationDbContext dbContext,
    IValidator<TransactionRequest> validator)
{
    private const int BatchSize = 750;
    private const int LoadGenerationCount = 100_000;
    private const string UniqueViolationSqlState = "23505";
    private static readonly string[] LoadCurrencies = ["USD", "EUR", "UAH"];
    private static readonly string[] LoadSourceChannels = ["WEB", "MOBILE", "API"];

    public async Task<TransactionResponse> IngestAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var normalizedRequest = await ValidateAndNormalizeAsync(request, cancellationToken);
        var key = DuplicateTransactionKey.FromRequest(normalizedRequest);

        if (await ExistsAsync(key, cancellationToken))
        {
            throw new DuplicateTransactionException();
        }

        var transaction = CreateTransaction(normalizedRequest);
        dbContext.Transactions.Add(transaction);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
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

        var acceptedCount = 0;
        var errors = new List<RowError>();
        var pendingBatch = new List<(int RowNumber, TransactionRequest Request)>(BatchSize);
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
                errors.Add(new RowError(rowNumber, "CSV_PARSE_ERROR", "CSV row could not be parsed."));
                continue;
            }

            var request = new TransactionRequest(
                csvRow.CustomerId,
                csvRow.TransactionDate,
                csvRow.Amount,
                csvRow.Currency,
                csvRow.SourceChannel);

            TransactionRequest normalizedRequest;
            try
            {
                normalizedRequest = await ValidateAndNormalizeAsync(request, cancellationToken);
            }
            catch (AppValidationException exception)
            {
                errors.Add(new RowError(rowNumber, "VALIDATION_ERROR", "Invalid transaction.", exception.Errors));
                continue;
            }

            var duplicateKey = DuplicateTransactionKey.FromRequest(normalizedRequest);
            if (!duplicateKeysSeenInFile.Add(duplicateKey))
            {
                errors.Add(new RowError(rowNumber, "DUPLICATE_TRANSACTION", "Duplicate transaction in uploaded CSV."));
                continue;
            }

            pendingBatch.Add((rowNumber, normalizedRequest));

            if (pendingBatch.Count >= BatchSize)
            {
                acceptedCount += await SaveBatchAsync(pendingBatch, errors, cancellationToken);
                pendingBatch.Clear();
            }
        }

        if (pendingBatch.Count > 0)
        {
            acceptedCount += await SaveBatchAsync(pendingBatch, errors, cancellationToken);
        }

        return new BatchIngestResponse(acceptedCount, errors.Count, errors);
    }

    public async Task<GenerateLoadResponse> GenerateLoadAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalInserted = 0;
        var errors = new List<RowError>();
        var pendingBatch = new List<(int RowNumber, TransactionRequest Request)>(BatchSize);

        for (var index = 1; index <= LoadGenerationCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            pendingBatch.Add((index, GenerateTransactionRequest(index)));

            if (pendingBatch.Count >= BatchSize)
            {
                totalInserted += await SaveBatchAsync(pendingBatch, errors, cancellationToken);
                pendingBatch.Clear();
            }
        }

        if (pendingBatch.Count > 0)
        {
            totalInserted += await SaveBatchAsync(pendingBatch, errors, cancellationToken);
        }

        stopwatch.Stop();

        return new GenerateLoadResponse(
            LoadGenerationCount,
            totalInserted,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task<int> SaveBatchAsync(
        IReadOnlyList<(int RowNumber, TransactionRequest Request)> batchRows,
        List<RowError> errors,
        CancellationToken cancellationToken)
    {
        var duplicateKeys = batchRows.Select(batchRow => DuplicateTransactionKey.FromRequest(batchRow.Request)).ToList();
        var existingDuplicateKeys = await LoadExistingDuplicateKeysAsync(duplicateKeys, cancellationToken);
        var transactionsToInsert = new List<Transaction>(batchRows.Count);

        foreach (var batchRow in batchRows)
        {
            var duplicateKey = DuplicateTransactionKey.FromRequest(batchRow.Request);
            if (existingDuplicateKeys.Contains(duplicateKey))
            {
                errors.Add(new RowError(batchRow.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
                continue;
            }

            transactionsToInsert.Add(CreateTransaction(batchRow.Request));
        }

        if (transactionsToInsert.Count == 0)
        {
            return 0;
        }

        dbContext.Transactions.AddRange(transactionsToInsert);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return transactionsToInsert.Count;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await SaveRowsIndividuallyAfterConflictAsync(batchRows, errors, cancellationToken);
        }
    }

    private static TransactionRequest GenerateTransactionRequest(int index)
    {
        var transactionDate = DateTimeOffset.UtcNow.AddSeconds(-Random.Shared.Next(0, 30 * 24 * 60 * 60));
        var amount = Random.Shared.Next(100, 100_001) / 100m;

        return new TransactionRequest(
            $"TEST-CUST-{index:D6}",
            transactionDate,
            amount,
            LoadCurrencies[Random.Shared.Next(LoadCurrencies.Length)],
            LoadSourceChannels[Random.Shared.Next(LoadSourceChannels.Length)]);
    }

    private async Task<int> SaveRowsIndividuallyAfterConflictAsync(
        IReadOnlyList<(int RowNumber, TransactionRequest Request)> batchRows,
        List<RowError> errors,
        CancellationToken cancellationToken)
    {
        var acceptedCount = 0;

        foreach (var batchRow in batchRows)
        {
            var duplicateKey = DuplicateTransactionKey.FromRequest(batchRow.Request);
            if (await ExistsAsync(duplicateKey, cancellationToken))
            {
                errors.Add(new RowError(batchRow.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
                continue;
            }

            dbContext.Transactions.Add(CreateTransaction(batchRow.Request));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                acceptedCount++;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                dbContext.ChangeTracker.Clear();
                errors.Add(new RowError(batchRow.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
            }
        }

        return acceptedCount;
    }

    private async Task<HashSet<DuplicateTransactionKey>> LoadExistingDuplicateKeysAsync(
        IReadOnlyCollection<DuplicateTransactionKey> duplicateKeys,
        CancellationToken cancellationToken)
    {
        if (duplicateKeys.Count == 0)
        {
            return [];
        }

        var customerIds = duplicateKeys.Select(duplicateKey => duplicateKey.CustomerId).Distinct().ToArray();
        var transactionDates = duplicateKeys.Select(duplicateKey => duplicateKey.TransactionDate).Distinct().ToArray();
        var currencies = duplicateKeys.Select(duplicateKey => duplicateKey.Currency).Distinct().ToArray();
        var sourceChannels = duplicateKeys.Select(duplicateKey => duplicateKey.SourceChannel).Distinct().ToArray();

        var candidates = await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction =>
                customerIds.Contains(transaction.CustomerId) &&
                transactionDates.Contains(transaction.TransactionDate) &&
                currencies.Contains(transaction.Currency) &&
                sourceChannels.Contains(transaction.SourceChannel))
            .ToListAsync(cancellationToken);

        return candidates
            .Select(DuplicateTransactionKey.FromTransaction)
            .Where(duplicateKeys.Contains)
            .ToHashSet();
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
}
