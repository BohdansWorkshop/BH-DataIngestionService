using BH_DataIngestionService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BH_DataIngestionService.Infrastructure.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Transaction>(entity =>
        {
            entity.ToTable("transactions");
            entity.HasKey(transaction => transaction.Id);

            entity.Property(transaction => transaction.CustomerId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(transaction => transaction.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(transaction => transaction.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(transaction => transaction.SourceChannel)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(transaction => transaction.TransactionDate).IsRequired();
            entity.Property(transaction => transaction.CreatedAtUtc).IsRequired();

            entity.HasIndex(transaction => transaction.CustomerId);
            entity.HasIndex(transaction => transaction.TransactionDate);

            entity.HasIndex(transaction => new
                {
                    transaction.CustomerId,
                    transaction.TransactionDate,
                    transaction.Amount,
                    transaction.Currency,
                    transaction.SourceChannel
                })
                .IsUnique()
                .HasDatabaseName("ux_transactions_duplicate_key");
        });
    }
}
