using System.Data;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal enum CronType
{
    DefinedInSource,
    Manual
}

internal class CronJob
{
    internal Guid Id { get; set; } = Guid.NewGuid();
    internal string MethodName { get; set; } = "";
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DeferredJob? Current { get; set; } = null;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int? Minute { get; set; } = null;
    public int? Hour { get; set; } = null;
    public int? Day { get; set; } = null;
    public int? Month { get; set; } = null;
    public int? DayOfWeek { get; set; } = null;

    public bool Disabled { get; set; } = false;

    public CronType Type { get; set; } = CronType.DefinedInSource;

    private CronJob() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<CronJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.Created);
        model.HasOne(p => p.Current);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        model
            .Property(e => e.Type)
            .HasConversion(v => v.ToString(), v => (CronType)Enum.Parse(typeof(CronType), v));
    }

    static internal class Repository
    {
        // internal static
        static async internal Task Upsert(DbContext context, IEnumerable<CronJob> jobs)
        {
            var methodNames = jobs.Select(p => p.MethodName);
            var existingJobs = await context
                .Set<CronJob>()
                .Where(
                    p => methodNames.Contains(p.MethodName) && p.Type == CronType.DefinedInSource
                )
                .ToListAsync();

            foreach (var existingJob in existingJobs)
            {
                foreach (var newJob in jobs) { }
            }

            // return Task.CompletedTask;
        }
    }

    static internal IEnumerable<CronJob> CreateFromMethod(MethodInfo method)
    {
        var methodName = method.Name;
        var attributes = method.GetCustomAttributes<CronAttribute>();
        if (!attributes.Any())
        {
            throw new InvalidOperationException(
                $"CronAttribute must be set for method '{method.Name}'"
            );
        }

        return attributes.Select(
            p =>
                new CronJob()
                {
                    MethodName = methodName,
                    Minute = p.Minute,
                    Hour = p.Hour,
                    Day = p.Day,
                    Month = p.Month,
                    DayOfWeek = p.DayOfWeek,
                    // InputJson = JsonSerializer.Serialize(input),
                    // WaitBetweenAttempts = opts?.WaitBetweenAttempts ?? attribute.WaitBetweenAttempts,
                    // TimeLimit = opts?.TimeLimit ?? attribute.TimeLimit,
                    // MaxAttempts = opts?.MaxAttempts ?? attribute.MaxAttempts,
                    // AttemptAfter = opts?.AttemptAfter
                }
        );
    }
}
