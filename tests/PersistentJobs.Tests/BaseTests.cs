using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersistentJobs.Tests;
using Xunit.Abstractions;

public class BaseTests
{
    public readonly string TestName;

    public BaseTests(ITestOutputHelper output)
    {
        // Get the displayname of the current running test
        TestName =
            (
                (ITest)output
                    .GetType()
                    .GetField(
                        "test",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )! // private property test
                .GetValue(output)!
            ).DisplayName;
    }

    internal void Init()
    {
        var context = CreateContext();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    public DbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource={TestName}.sqlite")
            .EnableSensitiveDataLogging(true)
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
        var options = builder.Options;
        var context = new TestDbContext(options);
        return context;
    }
}
