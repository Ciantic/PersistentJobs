using System.Data;
using System.Dynamic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class DeferredJob
{
    internal Guid Id { get; private set; } = Guid.NewGuid();
    internal string MethodName { get; private set; } = "";
    internal DeferredStatus Status { get; private set; } = DeferredStatus.Waiting;
    private string? InputJson { get; set; } = "";
    private string? OutputJson { get; set; } = null;
    private TimeSpan? TimeLimit { get; set; } = null;
    private TimeSpan? WaitBetweenAttempts { get; set; } = null;
    private uint Attempts { get; set; } = 0;
    private uint MaxAttempts { get; set; } = 1;
    private DateTime? AttemptAfter { get; set; }
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DateTime? Queued { get; set; } = null;
    internal DateTime? Finished { get; set; } = null;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private DeferredJob() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<DeferredJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson);
        model.Property(p => p.OutputJson);
        model.Property(p => p.Created);
        model.Property(p => p.Queued);
        model.Property(p => p.Finished);
        model.Property(p => p.TimeLimit);
        model.Property(p => p.AttemptAfter);
        model.Property(p => p.Attempts);
        model.Property(p => p.WaitBetweenAttempts);
        model.Property(p => p.MaxAttempts);
        model
            .Property(e => e.Status)
            .HasConversion(
                v => v.ToString(),
                v => (DeferredStatus)Enum.Parse(typeof(DeferredStatus), v)
            );

        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
    }

    internal static class Repository
    {
        async static internal Task<DeferredJob> Get(DbContext context, Guid id)
        {
            var job = (
                await context.Set<DeferredJob>().Where(p => p.Id == id).FirstOrDefaultAsync()
            );
            if (job == null)
            {
                throw new ObjectNotFoundException();
            }
            return job;
        }

        async static internal Task<Output?> GetOutput<Output>(DbContext context, Guid id)
        {
            var json = (
                await context
                    .Set<DeferredJob>()
                    .Where(p => p.Id == id && p.Status == DeferredStatus.Succeeded)
                    .FirstOrDefaultAsync()
            );
            if (json == null)
            {
                throw new ObjectNotFoundException();
            }
            if (json.OutputJson == null)
            {
                return default;
            }
            return JsonSerializer.Deserialize<Output>(json.OutputJson);
        }

        async static internal Task<List<DeferredJob>> GetAvailable(
            DbContext context,
            Dictionary<string, uint> maxParallelizationByMethod
        )
        {
            IEnumerable<string> maxMethods = maxParallelizationByMethod.Keys;
            List<DeferredJob> jobs = new();

            foreach (var (method, maxP) in maxParallelizationByMethod)
            {
                jobs.AddRange(
                    (
                        await context
                            .Set<DeferredJob>()
                            .Where(
                                p =>
                                    p.MethodName == method
                                    && (
                                        p.Status == DeferredStatus.Queued
                                        || (
                                            p.Status == DeferredStatus.Waiting
                                            && (
                                                p.AttemptAfter <= DateTime.UtcNow
                                                || p.AttemptAfter == null
                                            )
                                        )
                                    )
                            )
                            .OrderBy(p => p.Created)
                            .Take((int)maxP)
                            .Where(p => p.Status == DeferredStatus.Waiting)
                            .ToListAsync()
                    )
                );
            }

            // Get all unstarted work items
            jobs.AddRange(
                await context
                    .Set<DeferredJob>()
                    .Where(
                        p =>
                            p.Status == DeferredStatus.Waiting
                            && (p.AttemptAfter <= DateTime.UtcNow || p.AttemptAfter == null)
                            && !maxMethods.Contains(p.MethodName)
                    )
                    .ToListAsync()
            );

            return jobs;
        }

        internal async static Task<Deferred> Insert(DbContext context, DeferredJob job)
        {
            await context.Set<DeferredJob>().AddAsync(job);
            return new Deferred(job);
        }

        internal async static Task<Deferred<O>> Insert<O>(DbContext context, DeferredJob job)
        {
            await context.Set<DeferredJob>().AddAsync(job);
            return new Deferred<O>(job);
        }

        [Serializable]
        internal class ObjectNotFoundException : Exception
        {
            internal ObjectNotFoundException() { }
        }
    }

    async internal Task<DeferredJobException[]> GetExceptions(DbContext context)
    {
        return await DeferredJobException.GetAllForJob(context, this);
    }

    internal TimeSpan GetTimeLimit()
    {
        return TimeLimit ?? TimeSpan.FromSeconds(0);
    }

    internal object? Queue(Type? inputType)
    {
        object? inputObject = null;
        if (Attempts >= MaxAttempts)
        {
            throw new InvalidOperationException(
                "Maximum retries exceeded, queueing is not allowed"
            );
        }

        if (AttemptAfter is not null && DateTime.UtcNow < AttemptAfter)
        {
            throw new InvalidOperationException("Job must be queued later");
        }

        if (Status != DeferredStatus.Waiting)
        {
            throw new InvalidOperationException("Job is not waiting to be queued");
        }

        if (inputType != null)
        {
            if (InputJson is null)
            {
                throw new InvalidOperationException(
                    "Input type is set, the input json must be set"
                );
            }

            try
            {
                inputObject = JsonSerializer.Deserialize(InputJson, inputType);
            }
            catch (JsonException)
            {
                throw new ArgumentException(
                    "Unable to serialize to given input type",
                    nameof(inputType)
                );
            }
        }

        Attempts += 1;
        Status = DeferredStatus.Queued;
        Queued = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
        return inputObject;
    }

    internal void Complete(object? outputValue)
    {
        if (Queued == null)
        {
            throw new InvalidOperationException("Complete does not work for non queued items");
        }

        if (Finished != null)
        {
            throw new InvalidOperationException("It's already finished");
        }

        Status = DeferredStatus.Succeeded;
        OutputJson = JsonSerializer.Serialize(outputValue);
        Finished = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    async internal Task InsertException(DbContext context, Exception exception)
    {
        if (Queued == null)
        {
            throw new InvalidOperationException("Only queued item can raise exceptions");
        }

        if (Finished != null)
        {
            throw new InvalidOperationException("Finished items can't raise exceptions");
        }

        Queued = null;
        if (Attempts >= MaxAttempts)
        {
            Status = DeferredStatus.Failed;
            Finished = DateTime.UtcNow;
        }
        else
        {
            Status = DeferredStatus.Waiting;
        }
        ConcurrencyStamp = Guid.NewGuid();
        if (WaitBetweenAttempts != null)
        {
            AttemptAfter = DateTime.UtcNow + WaitBetweenAttempts;
        }

        await DeferredJobException.Insert(context, this, exception);
    }

    static internal DeferredJob CreateFromMethod(
        MethodInfo method,
        object? input = null,
        DeferredOptions? opts = null
    )
    {
        var attribute =
            method.GetCustomAttribute<DeferredAttribute>()
            ?? throw new InvalidOperationException(
                $"DeferredAttribute must be set for method '{method.Name}'"
            );

        if (!method.IsStatic)
        {
            throw new InvalidOperationException("Persistent jobs work only on static methods.");
        }

        if (!method.IsPublic)
        {
            throw new InvalidOperationException(
                $"Deferred method '{method.Name}' needs to be public."
            );
        }

        var methodName = method.Name;
        return new DeferredJob()
        {
            MethodName = methodName,
            InputJson = JsonSerializer.Serialize(input),
            WaitBetweenAttempts = opts?.WaitBetweenAttempts ?? attribute.WaitBetweenAttempts,
            TimeLimit = opts?.TimeLimit ?? attribute.TimeLimit,
            MaxAttempts = opts?.MaxAttempts ?? attribute.MaxAttempts,
            AttemptAfter = opts?.AttemptAfter
        };
    }

    static internal DeferredJob CreateFromCronJob(CronJob job, DeferredOptions opts)
    {
        if (opts.AttemptAfter is null)
        {
            throw new InvalidOperationException(
                "Cron jobs must be scheduled to attempt after given time"
            );
        }

        return new DeferredJob()
        {
            MethodName = job.MethodName,
            InputJson = job.InputJson,
            WaitBetweenAttempts =
                opts?.WaitBetweenAttempts ?? job.SchedulerInstance?.WaitBetweenAttempts,
            TimeLimit = opts?.TimeLimit ?? job.SchedulerInstance?.TimeLimit,
            MaxAttempts = opts?.MaxAttempts ?? job.SchedulerInstance?.MaxAttempts ?? 1,
            AttemptAfter = opts?.AttemptAfter
        };
    }
}
