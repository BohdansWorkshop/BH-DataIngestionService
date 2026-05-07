using System.Text;
using BH_DataIngestionService.Application.DTOs;

namespace BH_DataIngestionService.Application.UnitTests.TestUtilities;

internal sealed class TransactionCsvStream : Stream
{
    private readonly IEnumerator<string> rows;
    private readonly Queue<byte> buffer = new();
    private bool disposed;

    public TransactionCsvStream(IEnumerable<TransactionRequest> transactions)
    {
        rows = BuildRows(transactions).GetEnumerator();
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] destination, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (offset < 0 || count < 0 || offset + count > destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        FillBuffer(count);

        var bytesRead = 0;
        while (bytesRead < count && buffer.Count > 0)
        {
            destination[offset + bytesRead] = buffer.Dequeue();
            bytesRead++;
        }

        return bytesRead;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FillBuffer(destination.Length);

        var bytesRead = 0;
        while (bytesRead < destination.Length && buffer.Count > 0)
        {
            destination.Span[bytesRead] = buffer.Dequeue();
            bytesRead++;
        }

        return ValueTask.FromResult(bytesRead);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            rows.Dispose();
            disposed = true;
        }

        base.Dispose(disposing);
    }

    private void FillBuffer(int requestedBytes)
    {
        while (buffer.Count < requestedBytes && rows.MoveNext())
        {
            foreach (var value in Encoding.UTF8.GetBytes(rows.Current))
            {
                buffer.Enqueue(value);
            }
        }
    }

    private static IEnumerable<string> BuildRows(IEnumerable<TransactionRequest> transactions)
    {
        yield return "CustomerId,TransactionDate,Amount,Currency,SourceChannel\r\n";

        foreach (var transaction in transactions)
        {
            yield return FormattableString.Invariant(
                $"{transaction.CustomerId},{transaction.TransactionDate:O},{transaction.Amount:0.00},{transaction.Currency},{transaction.SourceChannel}\r\n");
        }
    }
}
