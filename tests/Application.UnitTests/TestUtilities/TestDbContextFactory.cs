using BH_DataIngestionService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BH_DataIngestionService.Application.UnitTests.TestUtilities;

internal static class TestDbContextFactory
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
