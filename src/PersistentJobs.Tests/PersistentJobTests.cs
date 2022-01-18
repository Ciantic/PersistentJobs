using System;
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

public class ExampleJob : IJob<int, bool>
{
    public Task<IJobRunning<bool>> StartAsync(int input)
    {
        throw new System.NotImplementedException();
    }
}

public partial class Worker
{
    public class Input
    {
        public string Test { get; set; } = "";
        public string Other { get; set; } = "";
    }

    [Job]
    private static Task ExampleJobAsync(int input, DbContext dbContext)
    {
        return Task.CompletedTask;
    }

    public static int Foo(Input input, DbContext dbContext)
    {
        var jobs = dbContext.Set<PersistentJob>().ToArray();
        // var a = Worker.HelloWorld();
        // var foo = HelloWorld();
        // var zoo = Zoo_ExampleJobAsync();
        return jobs.Length;
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
    public DbContext Create()
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
    public void TestExecute()
    {
        var c = Create();

        var p = new PersistentJob()
        {
            AssemblyName = "PersistentJobs.Tests",
            ClassName = "PersistentJobs.Tests.Worker",
            MethodName = "Foo",
            InputJson = "{ \"Test\" : \"Foo\", \"Other\" : \"Great\" }"
        };

        var services = new ServiceCollection();
        services.AddSingleton(c);
        var provider = services.BuildServiceProvider();

        p.Execute(c, provider);
    }

    [Fact]
    public void Test1()
    {
        var context = Create();

        context.Add(new PersistentJob() { InputJson = "5" });
        context.SaveChanges();

        Assert.Equal(
            "5",
            context.Set<PersistentJob>().Where(p => p.InputJson == "5").First().InputJson
        );
    }
}
