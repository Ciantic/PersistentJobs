using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PersistentJobs.Tests;

public partial class Crons
{
    [CronJob(Minute = 0)]
    public async static Task<bool> TestEvenHours(CancellationToken cancellationToken = default)
    {
        await Task.Delay(5000, cancellationToken);
        return true;
    }
}

public class CronJobTests
{
    static internal void Init()
    {
        var context = CreateContext();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    static public DbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=cronjob.sqlite")
            .EnableSensitiveDataLogging(true);
        var options = builder.Options;
        var context = new TestDbContext(options);
        return context;
    }

    [Fact]
    async public void TestCronJob()
    {
        Init();
    }
}
