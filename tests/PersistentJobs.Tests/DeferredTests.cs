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
using Microsoft.Extensions.Logging;
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
    public static Task UnitJob()
    {
        return Task.CompletedTask;
    }

    [CreateDeferred(MaxAttempts = 3)]
    public static Task ExceptionJob()
    {
        throw new MyException("This is a known failure");
    }

    [CreateDeferred(MaxParallelizationCount = 1)]
    public static Task<bool> SingleRunningMethod()
    {
        return Task.FromResult(true);
    }

    [System.Serializable]
    public class MyException : System.Exception
    {
        public MyException(string message) : base(message) { }
    }
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
    static internal void Init()
    {
        var context = CreateContext();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    static public DbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=persistentjob.sqlite")
            .EnableSensitiveDataLogging(true)
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
        var options = builder.Options;
        var context = new TestDbContext(options);
        return context;
    }

    [Fact]
    async public void TestExampleJob()
    {
        Init();

        Deferred<int> deferred;
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
                var service = new DeferredQueue(opts: new(), provider);
                await service.ProcessAsync();
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            var output = await deferred.GetOutput(httpDbContext);
            Assert.Equal(42 + 5, output);
            Assert.Equal(DeferredStatus.Succeeded, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestCancellableJob()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred<bool> deferred;

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
                var service = new DeferredQueue(opts: new(), provider);
                var _ = service.ProcessAsync();

                await Task.Run(
                    async () =>
                    {
                        await Task.Delay(50);

                        // Ensure it is running
                        using (var httpDbContext = CreateContext())
                        {
                            Assert.Equal(
                                DeferredStatus.Queued,
                                await deferred.GetStatus(httpDbContext)
                            );
                        }

                        // Then stop it
                        //
                        // (this should cancel the CancellationToken given to
                        // the `AnotherJobCancellable`)
                        await service.CancelAsync();
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
            Assert.Equal(DeferredStatus.Failed, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestUnit()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred deferred;

        using (var httpDbContext = CreateContext())
        {
            deferred = await Worker.UnitJobDeferred(httpDbContext);
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
                var service = new DeferredQueue(opts: new(), provider);
                await service.ProcessAsync();
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            Assert.Equal(DeferredStatus.Succeeded, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestExceptions()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred deferred;

        using (var httpDbContext = CreateContext())
        {
            deferred = await Worker.ExceptionJobDeferred(httpDbContext);
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
                var service = new DeferredQueue(opts: new(), provider);
                await service.ProcessAsync();
                using (var httpDbContext = CreateContext())
                {
                    Assert.Equal(DeferredStatus.Waiting, await deferred.GetStatus(httpDbContext));
                }
                await service.ProcessAsync();
                using (var httpDbContext = CreateContext())
                {
                    Assert.Equal(DeferredStatus.Waiting, await deferred.GetStatus(httpDbContext));
                }
                await service.ProcessAsync();
            }
        );

        // Then checkout the exception, and ensure it has failed
        using (var httpDbContext = CreateContext())
        {
            // Failed
            Assert.Equal(DeferredStatus.Failed, await deferred.GetStatus(httpDbContext));

            // With three exceptions
            var exceptions = await deferred.GetExceptions(httpDbContext);
            Assert.Equal(3, exceptions.Length);

            // Where exception is MyException
            var first = exceptions.First();
            Assert.Equal("PersistentJobs.Tests.Worker+MyException", first.Name);
        }
    }

    [Fact]
    async public void TestMaxParallelizationByMethod()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred d1,
            d2,
            d3,
            d4;

        using (var httpDbContext = CreateContext())
        {
            d1 = await Worker.SingleRunningMethodDeferred(httpDbContext);
            d2 = await Worker.SingleRunningMethodDeferred(httpDbContext);
            d3 = await Worker.SingleRunningMethodDeferred(httpDbContext);
            d4 = await Worker.SingleRunningMethodDeferred(httpDbContext);
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
                var service = new DeferredQueue(opts: new(), provider);
                await service.ProcessAsync();
                using (var httpDbContext = CreateContext())
                {
                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d4.GetStatus(httpDbContext));
                }
                await service.ProcessAsync();
                using (var httpDbContext = CreateContext())
                {
                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d4.GetStatus(httpDbContext));
                }
            }
        );
    }
    // TODO: Test WaitBetweenAttempts

}
