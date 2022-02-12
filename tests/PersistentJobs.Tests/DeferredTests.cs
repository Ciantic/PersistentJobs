using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static PersistentJobs.Tests.Worker;

namespace PersistentJobs.Tests;

public partial class Worker
{
    public class Input
    {
        public string First { get; set; } = "";
        public string Second { get; set; } = "";
    }

    [CreateDeferred]
    public static Task<Input> ExampleJob(Input input, DbContext dbContext)
    {
        return Task.FromResult(
            new Input() { First = input.First + " Append 1", Second = input.Second + " Append 2" }
        );
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

    [CreateDeferred(MaxAttempts = 100)]
    public static Task JobCancelsPermanently()
    {
        throw new DeferredCanceledException("I was self cancelled");
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

    [CreateDeferred(MaxParallelizationCount = 2)]
    public static Task<bool> SingleRunningMethod2()
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

public class PersistentJobTests : BaseTests
{
    public PersistentJobTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    async public void TestService()
    {
        var services = ConfigureServices();
        using var scope = services.CreateScope();

        using var context = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<DbContext>>()
            .CreateDbContextAsync();

        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    async public void TestExampleJob()
    {
        Init();

        Deferred<Input> deferred;
        // Http runs in own thread and scope, creates a deferred task
        using (var httpDbContext = CreateContext())
        {
            deferred = ExampleJobDeferred(
                new() { First = "First", Second = "Second" },
                httpDbContext
            );
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);
                using var context = CreateContext();
                await service.ProcessAsync(context);
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            var output = await deferred.GetOutput(httpDbContext);
            Assert.Equal("First Append 1", output.First);
            Assert.Equal("Second Append 2", output.Second);
            Assert.Equal(DeferredStatus.Succeeded, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestCancelFromInsideJob()
    {
        Init();

        Deferred deferred;
        // Http runs in own thread and scope, creates a deferred task
        using (var httpDbContext = CreateContext())
        {
            deferred = JobCancelsPermanentlyDeferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);
                using var context = CreateContext();
                await service.ProcessAsync(context);
            }
        );

        // Then user wants to look at the value
        using (var httpDbContext = CreateContext())
        {
            var output = await deferred.GetStatus(httpDbContext);
            Assert.Equal(DeferredStatus.Canceled, await deferred.GetStatus(httpDbContext));
        }
    }

    [Fact]
    async public void TestQueueCancellation()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred<bool> deferred;

        using (var httpDbContext = CreateContext())
        {
            deferred = AnotherJobCancellableDeferred(42, httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);
                using (var context = CreateContext())
                {
                    var _ = service.ProcessAsync(context);
                }

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
            deferred = UnitJobDeferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);
                using var context = CreateContext();
                await service.ProcessAsync(context);
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
            deferred = ExceptionJobDeferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);

                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);
                    Assert.Equal(DeferredStatus.Waiting, await deferred.GetStatus(httpDbContext));
                }

                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);
                    Assert.Equal(DeferredStatus.Waiting, await deferred.GetStatus(httpDbContext));
                    await service.ProcessAsync(httpDbContext);
                }
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
            d1 = SingleRunningMethodDeferred(httpDbContext);
            d2 = SingleRunningMethodDeferred(httpDbContext);
            d3 = SingleRunningMethodDeferred(httpDbContext);
            d4 = SingleRunningMethodDeferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);

                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);
                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d4.GetStatus(httpDbContext));
                }

                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);
                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d4.GetStatus(httpDbContext));
                }
            }
        );
    }

    [Fact]
    async public void TestMaxParallelizationByMethod2()
    {
        Init();

        // Http runs in own thread and scope, creates a deferred task
        Deferred d1,
            d2,
            d3,
            d4;

        using (var httpDbContext = CreateContext())
        {
            d1 = SingleRunningMethod2Deferred(httpDbContext);
            d2 = SingleRunningMethod2Deferred(httpDbContext);
            d3 = SingleRunningMethod2Deferred(httpDbContext);
            d4 = SingleRunningMethod2Deferred(httpDbContext);
            await httpDbContext.SaveChangesAsync();
        }

        // Sometime later, the service runs the deferred tasks autonomusly (to
        // speed things up we call it manually)
        await Task.Run(
            async () =>
            {
                // Background service runs in own thread and scope
                var provider = ConfigureServices();
                var service = new DeferredQueue(opts: new(), provider);
                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);

                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Waiting, await d4.GetStatus(httpDbContext));
                }

                using (var httpDbContext = CreateContext())
                {
                    await service.ProcessAsync(httpDbContext);
                    Assert.Equal(DeferredStatus.Succeeded, await d1.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d2.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d3.GetStatus(httpDbContext));
                    Assert.Equal(DeferredStatus.Succeeded, await d4.GetStatus(httpDbContext));
                }
            }
        );
    }
    // TODO: Test WaitBetweenAttempts

}
