using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Sdk;

namespace PersistentJobs.Tests;

public partial class Worker
{
    public record Input
    {
        public string First { get; set; } = "";
        public string Second { get; set; } = "";
    }

    [CreateDeferred]
    public static Task<int> ExampleJob(int input, DbContext dbContext)
    {
        return Task.FromResult(input + 5);
    }

    [CreateDeferred]
    public async static Task<bool> AnotherJobCancellable(
        int input,
        DbContext dbContext,
        CancellationToken cancellationToken = default
    )
    {
        await Task.Delay(5000, cancellationToken);
        return true;
    }

    [CreateDeferred]
    public async static Task UnitJob() { }
}

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddPersistentJobs();

        base.OnModelCreating(modelBuilder);
    }
}

public class PersistentJobTests
{
    static public void Init()
    {
        var options =
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(
                "DataSource=test.sqlite"
            ).Options;

        // Create the schema in the database
        var context = new TestDbContext(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    static public DbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(
                "DataSource=test.sqlite"
            ).Options;

        // Create the schema in the database
        var context = new TestDbContext(options);
        return context;
    }

    [Fact]
    async public void TestExampleJob()
    {
        Init();

        DeferredTask<int> deferred;
        // Http runs in own thread and scope, creates a deferred task
        using (var httpDbContext = CreateContext())
        {
            deferred = await Worker.ExampleJobDeferred(42, httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var services = new ServiceCollection();
                services.AddScoped((pr) => CreateContext());
                var provider = services.BuildServiceProvider();
                var service = new JobService(opts: new(), provider);
                await service.RunAsync();
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            var output = await deferred.GetOutput(httpDbContext);
            Assert.Equal(42 + 5, output);
            Assert.Equal(DeferredTask.Status.Completed, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestCancellableJob()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        DeferredTask<bool> deferred;

        using (var httpDbContext = CreateContext())
        {
            deferred = await Worker.AnotherJobCancellableDeferred(42, httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var services = new ServiceCollection();
                services.AddScoped((pr) => CreateContext());
                var provider = services.BuildServiceProvider();
                var service = new JobService(opts: new(), provider);
                var _ = service.RunAsync();

                await Task.Run(
                    async () =>
                    {
                        await Task.Delay(500);

                        // Ensure it is running
                        using (var httpDbContext = CreateContext())
                        {
                            Assert.Equal(
                                DeferredTask.Status.Running,
                                await deferred.GetStatus(httpDbContext)
                            );
                        }

                        // Then stop it
                        //
                        // (this should cancel the CancellationToken given to
                        // the `AnotherJobCancellable`)
                        await service.StopAsync();
                    }
                );
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            var exceptions = await deferred.GetExceptions(httpDbContext);
            var first = exceptions.First();
            Assert.Equal("System.Threading.Tasks.TaskCanceledException", first.Name);
            Assert.Equal(DeferredTask.Status.Waiting, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestUnit()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        DeferredTask deferred;

        using (var httpDbContext = CreateContext())
        {
            deferred = await Worker.UnitJobDeferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        var startTask = Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var services = new ServiceCollection();
                services.AddScoped((pr) => CreateContext());
                var provider = services.BuildServiceProvider();
                var service = new JobService(opts: new(), provider);
                await service.RunAsync();
            }
        );

        await startTask;

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            Assert.Equal(DeferredTask.Status.Completed, await deferred.GetStatus(httpDbContext));
        }
    }
}
