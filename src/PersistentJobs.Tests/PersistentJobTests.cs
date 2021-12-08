using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
    [Job]
    private static Task ExampleJobAsync(int input, DbContext dbContext)
    {
        return Task.CompletedTask;
    }
}

public class PersistentJobTests
{
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddPersistentJobs();

            base.OnModelCreating(modelBuilder);
        }
    }

    [Fact]
    public void Test1()
    {
        var options =
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(
                "DataSource=test.sqlite"
            ).Options;

        // Create the schema in the database
        using var context = new TestDbContext(options);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();

        context.Add(new PersistentJob() { InputJson = "5" });
        context.SaveChanges();

        Assert.Equal(
            "5",
            context.Set<PersistentJob>().Where(p => p.InputJson == "5").First().InputJson
        );
    }
}
