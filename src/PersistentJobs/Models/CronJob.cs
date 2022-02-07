using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using static PersistentJobs.CronService;

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
    internal MethodInfo? Method { get; set; }
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DateTime? InSource { get; set; } = DateTime.UtcNow;
    private DeferredJob? Current { get; set; } = null;
    internal int? Minute { get; set; } = null;
    internal int? Hour { get; set; } = null;
    internal int? Day { get; set; } = null;
    internal int? Month { get; set; } = null;
    internal int? DayOfWeek { get; set; } = null;
    private bool Disabled { get; set; } = false;

    private CronType Type { get; set; } = CronType.DefinedInSource;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private (string, int?, int?, int?, int?, int?) Key
    {
        get { return (MethodName, Minute, Hour, Day, Month, DayOfWeek); }
    }

    internal void Tick()
    {
        if (Current is null) { }
    }

    // private DateTime GetNextTimestamp(DateTime from) {
    //     if (Minute is not null && ) {

    //     }
    // }

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
        model
            .HasIndex(p => new { p.MethodName, p.Minute, p.Hour, p.Day, p.Month, p.DayOfWeek })
            .IsUnique();
    }

    static internal class Repository
    {
        // internal static
        static async internal Task<List<CronJob>> UpdateOrCreate(
            DbContext context,
            IEnumerable<CronJob> partialDefinedJobs
        )
        {
            var storedJobs = new List<CronJob>();
            var methodNames = partialDefinedJobs.Select(p => p.MethodName);
            var existingJobsDict = new Dictionary<
                (string, int?, int?, int?, int?, int?),
                CronJob
            >();
            var existingJobs = await context
                .Set<CronJob>()
                .Where(
                    p => methodNames.Contains(p.MethodName) && p.Type == CronType.DefinedInSource
                )
                .ToListAsync();

            foreach (var j in existingJobs)
            {
                existingJobsDict[j.Key] = j;
            }

            foreach (var j in partialDefinedJobs)
            {
                var ej = existingJobsDict.GetValueOrDefault(j.Key);
                if (ej != null)
                {
                    // Existing jobs
                    ej.InSource = DateTime.UtcNow;
                    storedJobs.Add(ej);
                }
                else
                {
                    // Non-existing jobs, create job
                    storedJobs.Add(j);
                    await context.Set<CronJob>().AddAsync(j);
                }
            }

            await context.SaveChangesAsync();
            return storedJobs;
        }
    }

    internal static CronJob CreateFromMethod(MethodInfo method, CronAttribute attr)
    {
        var j = new CronJob()
        {
            MethodName = method.Name,
            Method = method,
            Minute = attr.Minute,
            Hour = attr.Hour,
            Day = attr.Day,
            Month = attr.Month,
            DayOfWeek = attr.DayOfWeek
        };
        return j;
    }
}
