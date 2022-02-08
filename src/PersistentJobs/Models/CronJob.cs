using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text.Json;
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
    internal string? InputJson { get; set; } = null;
    internal string Scheduler { get; set; } = "";
    internal CronScheduler? SchedulerInstance { get; set; } = null;
    internal string? SchedulerJson { get; set; } = null;
    private bool Disabled { get; set; } = false;
    private CronType Type { get; set; } = CronType.DefinedInSource;
    private DeferredJob? Current { get; set; } = null;
    private DateTime? InSource { get; set; } = DateTime.UtcNow;
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private (string, string, string?) Key
    {
        get { return (MethodName, Scheduler, SchedulerJson); }
    }

    internal async Task Schedule(DbContext context)
    {
        if (SchedulerInstance is null)
        {
            throw new InvalidOperationException(
                "CronJob can only be scheduled if the SchedulerInstance is set"
            );
        }
        if (Method is null)
        {
            throw new InvalidOperationException(
                "CronJob can only be scheduled if the Method is set"
            );
        }

        if (Disabled)
        {
            return;
        }

        if (Current is null)
        {
            var runNextTime = SchedulerInstance.GetNextOccurrence(DateTime.UtcNow);
            if (runNextTime is not null && runNextTime.HasValue)
            {
                Current =
                    (await DeferredQueue.EnqueueCronjob(context, this, (DateTime)runNextTime)).Job;
                ConcurrencyStamp = Guid.NewGuid();
            }
        }
        else
        {
            // await context.Entry(Current).ReloadAsync();
            if (Current.Finished is not null)
            {
                var runNextTime = SchedulerInstance.GetNextOccurrence(DateTime.UtcNow);
                if (runNextTime is not null && runNextTime.HasValue)
                {
                    Current =
                        (
                            await DeferredQueue.EnqueueCronjob(context, this, (DateTime)runNextTime)
                        ).Job;
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
        model.Property(p => p.InputJson);
        model.Property(p => p.Created);
        model.HasOne(p => p.Current);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        model.Property(p => p.Scheduler);
        model.Property(p => p.SchedulerJson);
        model.Property(p => p.InSource);
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
            IEnumerable<CronJob> partialDefinedJobs,
            bool initial,
            Dictionary<string, Type> schedulers
        )
        {
            var storedJobs = new List<CronJob>();
            var methodNames = partialDefinedJobs.Select(p => p.MethodName);
            var existingJobsDict = new Dictionary<(string, string, string?), CronJob>();
            var existingJobs = await context.Set<CronJob>().ToListAsync();

            // TODO: Instantiate manual cron jobs

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
                    if (initial)
                    {
                        ej.InSource = DateTime.UtcNow;
                    }
                    ej.SchedulerInstance = j.SchedulerInstance;
                    ej.Method = j.Method;
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

    internal static CronJob CreateFromMethod(MethodInfo method, CronScheduler attr)
    {
        var j = new CronJob()
        {
            MethodName = method.Name,
            Method = method,
            Scheduler = attr.GetName(),
            SchedulerJson = attr.Serialize(),
            SchedulerInstance = attr
        };
        return j;
    }
}
