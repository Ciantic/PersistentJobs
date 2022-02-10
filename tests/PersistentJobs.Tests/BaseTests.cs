using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersistentJobs.Tests;
using Xunit.Abstractions;

public class DbDowncastFactory<T> : IDbContextFactory<DbContext> where T : DbContext
{
    private readonly IDbContextFactory<T> opts;

    public DbDowncastFactory(IDbContextFactory<T> opts)
    {
        this.opts = opts;
    }

    public DbContext CreateDbContext()
    {
        return opts.CreateDbContext();
    }
}

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
        var builder = new DbContextOptionsBuilder<TestDbContext>();
        Opts(builder);
        var options = builder.Options;
        var context = new TestDbContext(options);
        return context;
    }

    public IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Application specific
        services.AddDbContextFactory<TestDbContext>(Opts);

        // Generic version, used by the deferred tasks
        services.AddDbContextFactory<DbContext, DbDowncastFactory<TestDbContext>>();

        return services.BuildServiceProvider();
    }

    private void Opts(DbContextOptionsBuilder options)
    {
        options
            .UseSqlite($"DataSource={TestName}.sqlite")
            .EnableSensitiveDataLogging(true)
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
    }
}
