using System.Globalization;
using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Exceptions;
using BH_DataIngestionService.Application.Services.Ingestion.DTO;
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

    public async Task<TransactionResponse> IngestAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var normalizedRequest = await ValidateAndNormalizeAsync(request, cancellationToken);
        var transaction = CreateTransaction(normalizedRequest);

        try
        {
            dbContext.Transactions.Add(transaction);
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
        using var csv = new CsvReader(reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim, MissingFieldFound = null, HeaderValidated = null
            });

        var batchState = new BatchIngestionState();
        var duplicateKeysSeenInFile = new HashSet<DuplicateTransactionKey>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowNumber = csv.Parser.Row;
            BatchTransactionRow csvRow;

            try
            {
                csvRow = csv.GetRecord<BatchTransactionRow>();
            }
            catch (Exception exception) when (exception is CsvHelperException or FormatException)
            {
                batchState.Errors.Add(new RowError(rowNumber, IngestionErrorsConstants.CsvParseErrorTopic,
                    IngestionErrorsConstants.CsvParseErrorMessage));
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
                IngestionErrorsConstants.DuplicateInCsvMessage,
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
        var duplicateKeysSeenInBatch = new HashSet<DuplicateTransactionKey>();

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            await AddValidRowAsync(
                rowNumber,
                request,
                duplicateKeysSeenInBatch,
                IngestionErrorsConstants.DuplicateInBatchMessage,
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
            batchState.Errors.Add(new RowError(rowNumber, IngestionErrorsConstants.ValidationErrorTopic,
                IngestionErrorsConstants.InvalidTransactionMessage,
                exception.Errors));
            return;
        }

        var duplicateKey = DuplicateTransactionKey.FromRequest(normalizedRequest);
        if (!duplicateKeysSeen.Add(duplicateKey))
        {
            batchState.Errors.Add(new RowError(rowNumber, IngestionErrorsConstants.DuplicateTransactionTopic,
                duplicateMessage));
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
            return;

        batchState.AcceptedCount += await SaveBatchAsync(batchState.PendingRows, batchState.Errors, cancellationToken);
        batchState.PendingRows.Clear();
    }

    private async Task<int> SaveBatchAsync(
        IReadOnlyList<PendingTransactionRow> batchRows,
        ICollection<RowError> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await InsertBatchAsync(batchRows, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return await SaveRowsAfterBatchConflictAsync(batchRows, errors, cancellationToken);
        }
    }

    private async Task<int> InsertBatchAsync(
        IReadOnlyList<PendingTransactionRow> batchRows,
        CancellationToken cancellationToken)
    {
        if (batchRows.Count == 0)
            return 0;

        dbContext.Transactions.AddRange(
            batchRows.Select(x => CreateTransaction(x.Request)));

        await dbContext.SaveChangesAsync(cancellationToken);

        return batchRows.Count;
    }

    private async Task<int> SaveRowsAfterBatchConflictAsync(
        IReadOnlyList<PendingTransactionRow> batchRows,
        ICollection<RowError> errors,
        CancellationToken cancellationToken)
    {
        var acceptedCount = 0;

        foreach (var row in batchRows)
        {
            dbContext.Transactions.Add(CreateTransaction(row.Request));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                acceptedCount++;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                errors.Add(new RowError(row.RowNumber, IngestionErrorsConstants.DuplicateTransactionTopic,
                    IngestionErrorsConstants.DuplicateTransactionMessage));
                dbContext.ChangeTracker.Clear();
            }
        }

        return acceptedCount;
    }

    private async Task<TransactionRequest> ValidateAndNormalizeAsync(
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new AppValidationException(IngestionErrorsConstants.InvalidTransactionMessage,
                validationResult.ToErrorDictionary());
        }

        return request with
        {
            CustomerId = request.CustomerId.Trim(),
            Currency = request.Currency!.Trim().ToUpperInvariant(),
            SourceChannel = request.SourceChannel!.Trim()
        };
    }

    private static Transaction CreateTransaction(TransactionRequest request)
    {
        return new Transaction
        {
            CustomerId = request.CustomerId,
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
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}
