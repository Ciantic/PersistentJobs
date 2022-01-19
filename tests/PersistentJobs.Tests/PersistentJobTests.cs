using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Net.Security;
using System.Runtime.InteropServices;
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
    async public void TestExecution()
    {
        var c = Create();

        var p = new PersistentJob()
        {
            AssemblyName = "PersistentJobs.Tests",
            ClassName = "PersistentJobs.Tests.Worker",
            MethodName = "UnmarkedMethod",
            InputJson = "{ \"First\" : \"Foo\", \"Second\" : \"Bar\" }"
        };

        var services = new ServiceCollection();
        services.AddSingleton(c);
        var provider = services.BuildServiceProvider();

        object? output = await p.Execute(c, provider);
        Assert.Equal("Foo Bar", output);
    }

    [Fact]
    async public void Test1()
    {
        var context = Create();

        Worker.ExampleJobDeferred(42, context);
        await PersistentJob.InsertJob2(context, Worker.ExampleJob, 5);
        // PersistentJob.InsertJob(context, )

        // context.Add(new PersistentJob() { InputJson = "5" });
        // context.SaveChanges();

        // Assert.Equal(
        //     "5",
        //     context.Set<PersistentJob>().Where(p => p.InputJson == "5").First().InputJson
        // );
    }
}
