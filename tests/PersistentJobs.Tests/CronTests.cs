using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace PersistentJobs.Tests;

public partial class Crons
{
    public static int Ran = 0;

    [CronHourly(Minute = 12)]
    public async static Task<bool> TestEvenHours()
    {
        Ran += 1;
        return await Task.FromResult(true);
    }

    [Deferred]
    public async static Task DoSomething() { }
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
        var defqueue = new DeferredQueue(new DeferredQueue.DeferredQueueOpts(), provider);
        using (var httpDbContext = CreateContext())
        {
            await service.ProcessAsync(httpDbContext);
            await defqueue.ProcessAsync();
            Assert.Equal(1, Crons.Ran);
        }
        using (var httpDbContext = CreateContext())
        {
            await service.ProcessAsync(httpDbContext);
            await defqueue.ProcessAsync();
            Assert.Equal(2, Crons.Ran);
        }
    }
}
