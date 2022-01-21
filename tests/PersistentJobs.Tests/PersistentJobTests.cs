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

    public static string UnmarkedMethod(Input input, DbContext dbContext)
    {
        // var jobs = dbContext.Set<PersistentJob>().ToArray();
        // return jobs.Length;
        return input.First + " " + input.Second;
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
    static public DbContext Create()
    {
        var options =
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(
                "DataSource=test.sqlite"
            ).Options;

        // Create the schema in the database
        var context = new TestDbContext(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    async public void TestService()
    {
        var context = Create();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        var provider = services.BuildServiceProvider();
        var service = new JobService(provider);
        var deferred = await Worker.ExampleJobDeferred(42, context);
        await service.RunAsync();
        await PersistentJob.GotIt(context, deferred.Id);
    }


}
