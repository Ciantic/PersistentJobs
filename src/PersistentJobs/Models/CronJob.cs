using System.Reflection;
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
    private Guid Id { get; set; } = Guid.NewGuid();
    internal string MethodName { get; private set; } = "";

    internal JsonDocument? InputJson { get; private set; } = null;
    private string Scheduler { get; set; } = "";
    internal CronScheduler? SchedulerInstance { get; private set; } = null;
    private string SchedulerJson { get; set; } = "{}";
    private bool Disabled { get; set; } = false;
    private CronType Type { get; set; } = CronType.DefinedInSource;
    private DeferredJob? Current { get; set; } = null;
    private DateTime? LastInstantiated { get; set; } = DateTime.UtcNow;
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private (string, string, string?) Key
    {
        get { return (MethodName, Scheduler, SchedulerJson); }
    }

    internal void Schedule(DbContext context, IServiceProvider services)
    {
        if (SchedulerInstance is null)
        {
            throw new InvalidOperationException(
                "CronJob can only be scheduled if the SchedulerInstance is set"
            );
        }

        if (Disabled)
        {
            return;
        }

        if (Current is null)
        {
            var runNextTime = SchedulerInstance.GetNextOccurrence(
                DateTime.UtcNow,
                context,
                services
            );
            if (runNextTime is not null && runNextTime.HasValue)
            {
                Current = DeferredQueue.EnqueueCronjob(context, this, (DateTime)runNextTime).Job;
                ConcurrencyStamp = Guid.NewGuid();
            }
        }
        else
        {
            if (Current.Finished is not null)
            {
                var runNextTime = SchedulerInstance.GetNextOccurrence(
                    (DateTime)(
                        DateTime.UtcNow > Current.Finished ? DateTime.UtcNow : Current.Finished
                    ),
                    context,
                    services
                );
                if (runNextTime is not null && runNextTime.HasValue)
                {
                    Current =
                        DeferredQueue.EnqueueCronjob(context, this, (DateTime)runNextTime).Job;
                    ConcurrencyStamp = Guid.NewGuid();
                }
            }
        }
    }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<CronJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson).HasSqliteJsonDocumentConversion();
        model.Property(p => p.Created);
        model.HasOne(p => p.Current);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        model.Property(p => p.Disabled);
        model.Property(p => p.Scheduler);
        model.Property(p => p.SchedulerJson);
        model.Property(p => p.LastInstantiated);
        model
            .Property(e => e.Type)
            .HasConversion(v => v.ToString(), v => (CronType)Enum.Parse(typeof(CronType), v));
        model.HasIndex(p => new { p.MethodName, p.Scheduler, p.SchedulerJson }).IsUnique();
    }

    static internal class Repository
    {
        // internal static
        static async internal Task<List<CronJob>> UpdateOrCreate(
            DbContext context,
            IEnumerable<CronJob> currentJobs,
            bool initial,
            Dictionary<string, Type> schedulers
        )
        {
            var returnCronJobs = new List<CronJob>();
            var existingJobsDict = await context
                .Set<CronJob>()
                .ToDictionaryAsync(p => p.Key, p => p);

            // All current jobs, which still exist in the database
            foreach (var j in currentJobs)
            {
                existingJobsDict.Remove(j.Key, out var ej);
                if (ej != null)
                {
                    // Existing jobs
                    if (initial)
                    {
                        // On start, mark the existing cronjob as instantiated
                        ej.LastInstantiated = DateTime.UtcNow;
                    }
                    ej.SchedulerInstance = j.SchedulerInstance;
                    ej.ConcurrencyStamp = Guid.NewGuid();
                    returnCronJobs.Add(ej);
                }
                else
                {
                    // Non-existing jobs, create job
                    returnCronJobs.Add(j);
                    context.Set<CronJob>().Add(j);
                }
            }

            // Remaining jobs
            foreach (var j in existingJobsDict.Values)
            {
                var schedulerType = schedulers.GetValueOrDefault(j.Scheduler);
                if (initial && schedulerType is not null)
                {
                    // On start, mark the existing cronjob as instantiated
                    j.LastInstantiated = DateTime.UtcNow;
                    j.SchedulerInstance =
                        JsonSerializer.Deserialize(j.SchedulerJson, schedulerType) as CronScheduler;
                    j.ConcurrencyStamp = Guid.NewGuid();
                }

                returnCronJobs.Add(j);
            }

            return returnCronJobs;
        }
    }

    internal static CronJob CreateFromMethod(MethodInfo method, CronScheduler attr)
    {
        var j = new CronJob()
        {
            MethodName = method.Name,
            Scheduler = attr.GetName(),
            SchedulerJson = attr.Serialize(),
            SchedulerInstance = attr
        };
        return j;
    }
}
