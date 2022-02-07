using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace PersistentJobs.Tests;

public partial class Crons
{
    public static bool Ran = false;

    [Cron(Minute = 0)]
    public async static Task<bool> TestEvenHours()
    {
        Ran = true;
        return await Task.FromResult(true);
    }
}

public class CronTests
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
            .EnableSensitiveDataLogging(true)
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
        var options = builder.Options;
        var context = new TestDbContext(options);
        return context;
    }

    [Fact]
    async public void TestCronJob()
    {
        Init();
        var services = new ServiceCollection();
        services.AddScoped((pr) => CreateContext());
        var provider = services.BuildServiceProvider();

        var service = new CronService(provider);
        using (var httpDbContext = CreateContext())
        {
            await service.ProcessAsync(httpDbContext);
            Assert.True(Crons.Ran);
        }
    }
}
