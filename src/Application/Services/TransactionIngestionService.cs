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
    private const string UniqueViolationSqlState = "23505";

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
        var pending = new List<(int RowNumber, TransactionRequest Request)>(BatchSize);
        var seenInFile = new HashSet<DuplicateTransactionKey>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowNumber = csv.Context.Parser?.Row ?? 0;
            BatchTransactionRow row;

            try
            {
                row = csv.GetRecord<BatchTransactionRow>();
            }
            catch (Exception exception) when (exception is CsvHelperException or FormatException)
            {
                errors.Add(new RowError(rowNumber, "CSV_PARSE_ERROR", "CSV row could not be parsed."));
                continue;
            }

            var request = new TransactionRequest(
                row.CustomerId,
                row.TransactionDate,
                row.Amount,
                row.Currency,
                row.SourceChannel);

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

            var key = DuplicateTransactionKey.FromRequest(normalizedRequest);
            if (!seenInFile.Add(key))
            {
                errors.Add(new RowError(rowNumber, "DUPLICATE_TRANSACTION", "Duplicate transaction in uploaded CSV."));
                continue;
            }

            pending.Add((rowNumber, normalizedRequest));

            if (pending.Count >= BatchSize)
            {
                acceptedCount += await SaveBatchAsync(pending, errors, cancellationToken);
                pending.Clear();
            }
        }

        if (pending.Count > 0)
        {
            acceptedCount += await SaveBatchAsync(pending, errors, cancellationToken);
        }

        return new BatchIngestResponse(acceptedCount, errors.Count, errors);
    }

    private async Task<int> SaveBatchAsync(
        IReadOnlyList<(int RowNumber, TransactionRequest Request)> rows,
        List<RowError> errors,
        CancellationToken cancellationToken)
    {
        var keys = rows.Select(row => DuplicateTransactionKey.FromRequest(row.Request)).ToList();
        var existingKeys = await LoadExistingKeysAsync(keys, cancellationToken);
        var acceptedRows = new List<Transaction>(rows.Count);

        foreach (var row in rows)
        {
            var key = DuplicateTransactionKey.FromRequest(row.Request);
            if (existingKeys.Contains(key))
            {
                errors.Add(new RowError(row.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
                continue;
            }

            acceptedRows.Add(CreateTransaction(row.Request));
        }

        if (acceptedRows.Count == 0)
        {
            return 0;
        }

        dbContext.Transactions.AddRange(acceptedRows);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return acceptedRows.Count;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await SaveRowsIndividuallyAfterConflictAsync(rows, errors, cancellationToken);
        }
    }

    private async Task<int> SaveRowsIndividuallyAfterConflictAsync(
        IReadOnlyList<(int RowNumber, TransactionRequest Request)> rows,
        List<RowError> errors,
        CancellationToken cancellationToken)
    {
        var acceptedCount = 0;

        foreach (var row in rows)
        {
            var key = DuplicateTransactionKey.FromRequest(row.Request);
            if (await ExistsAsync(key, cancellationToken))
            {
                errors.Add(new RowError(row.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
                continue;
            }

            dbContext.Transactions.Add(CreateTransaction(row.Request));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                acceptedCount++;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                dbContext.ChangeTracker.Clear();
                errors.Add(new RowError(row.RowNumber, "DUPLICATE_TRANSACTION", "Transaction already exists."));
            }
        }

        return acceptedCount;
    }

    private async Task<HashSet<DuplicateTransactionKey>> LoadExistingKeysAsync(
        IReadOnlyCollection<DuplicateTransactionKey> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var customerIds = keys.Select(key => key.CustomerId).Distinct().ToArray();
        var currencies = keys.Select(key => key.Currency).Distinct().ToArray();
        var sourceChannels = keys.Select(key => key.SourceChannel).Distinct().ToArray();
        var dates = keys.Select(key => key.TransactionDate).Distinct().ToArray();

        var candidates = await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction =>
                customerIds.Contains(transaction.CustomerId) &&
                currencies.Contains(transaction.Currency) &&
                sourceChannels.Contains(transaction.SourceChannel) &&
                dates.Contains(transaction.TransactionDate))
            .ToListAsync(cancellationToken);

        return candidates
            .Select(DuplicateTransactionKey.FromTransaction)
            .Where(keys.Contains)
            .ToHashSet();
    }

    private Task<bool> ExistsAsync(DuplicateTransactionKey key, CancellationToken cancellationToken)
    {
        return dbContext.Transactions.AnyAsync(transaction =>
            transaction.CustomerId == key.CustomerId &&
            transaction.TransactionDate == key.TransactionDate &&
            transaction.Amount == key.Amount &&
            transaction.Currency == key.Currency &&
            transaction.SourceChannel == key.SourceChannel,
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
