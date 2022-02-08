using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PersistentJobs.Tests;

[AttributeUsage(AttributeTargets.Method)]
public class CronImmediately : CronScheduler
{
    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext context,
        IServiceProvider services
    )
    {
        return DateTime.UtcNow;
    }
}

public partial class Crons
{
    [CronImmediately]
    public async static Task<bool> TestScheduled()
    {
        return await Task.FromResult(true);
    }

    [Deferred]
    public static Task<bool> DoSomething()
    {
        return Task.FromResult(true);
    }
}

public class CronTests : BaseTests
{
    public CronTests(ITestOutputHelper output) : base(output) { }

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

            // Ensure cron service queued a single task
            var jobs = await DeferredJob.Repository.GetWithMethod(httpDbContext, "TestScheduled");
            Assert.Single(jobs);
            Assert.Equal(DeferredStatus.Waiting, jobs[0].Status);

            await defqueue.ProcessAsync(httpDbContext);
            var jobs2 = await DeferredJob.Repository.GetWithMethod(httpDbContext, "TestScheduled");

            // Ensure it has ran
            Assert.Single(jobs);
            Assert.Equal(DeferredStatus.Succeeded, jobs2[0].Status);
        }

        using (var httpDbContext = CreateContext())
        {
            await service.ProcessAsync(httpDbContext);
            await defqueue.ProcessAsync(httpDbContext);

            // Ensure that second is now scheduled
            var jobs = await DeferredJob.Repository.GetWithMethod(httpDbContext, "TestScheduled");
            Assert.Equal(2, jobs.Count);
            Assert.Equal(DeferredStatus.Succeeded, jobs[1].Status);
        }
    }

    [Fact]
    async public void TestManualCronJob()
    {
        Init();
        var services = new ServiceCollection();
        services.AddScoped((pr) => CreateContext());
        var provider = services.BuildServiceProvider();

        var service = new CronService(provider);
        var defqueue = new DeferredQueue(new(), provider);
        using (var httpDbContext = CreateContext())
        {
            await httpDbContext.Database.ExecuteSqlRawAsync(
                @"INSERT INTO CronJob (
                        Id,
                        ConcurrencyStamp,
                        Created,
                        LastInstantiated,
                        CurrentId,
                        Disabled,
                        MethodName,
                        Type,
                        SchedulerJson,
                        Scheduler,
                        InputJson
                    )
                    VALUES (
                        '1238E9CE-BA88-427F-A904-D17EB7A278C6',
                        '1230C2A8-E960-485A-9E56-C7DC240D22DC',
                        '2022-02-08 19:21:39.3046237',
                        NULL,
                        NULL,
                        0,
                        'DoSomething',
                        'Manual',
                        '{{""Minute"":12}}',
                        'CronImmediately',
                        NULL
                        
                    );",
                new List<object> { }
            );
        }
        using (var httpDbContext = CreateContext())
        {
            await service.ProcessAsync(httpDbContext);
            await defqueue.ProcessAsync(httpDbContext);
            var jobs = await DeferredJob.Repository.GetWithMethod(httpDbContext, "DoSomething");
            Assert.Single(jobs);
            Assert.Equal(DeferredStatus.Succeeded, jobs[0].Status);
        }
    }
}
