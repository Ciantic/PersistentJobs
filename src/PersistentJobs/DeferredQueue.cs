using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentJobs;

public class DeferredQueue
{
    private readonly TaskQueue queue;
    private readonly Dictionary<string, Invokable> methods = new();
    private readonly Dictionary<string, uint> maxParellelizationByMethod = new();
    private readonly IServiceProvider services;

    public record DeferredQueueOpts(int MaxParallelizationCount = 8)
    {
    }

    internal DeferredQueue(DeferredQueueOpts opts, IServiceProvider services)
    {
        queue = new(opts.MaxParallelizationCount);
        this.services = services;
        BuildMethodsCache();
    }

    public async Task CancelAsync()
    {
        queue.Cancel();
        await queue.Process();
    }

    public async Task ProcessAsync(DbContext context)
    {
        var availableJobs = await DeferredJob.Repository.GetAvailable(
            context,
            maxParellelizationByMethod
        );
        await ExecuteJobsAsync(context, availableJobs);
    }

    private async Task ExecuteJobsAsync(DbContext context, List<DeferredJob> jobs)
    {
        // Start and queue each work item
        foreach (var workitem in jobs)
        {
            var invokable = methods.GetValueOrDefault(workitem.MethodName);
            if (invokable is null)
            {
                // TODO: Corresponding method is not found from this assembly,
                // this is not an error, but needs to be logged as warning.
                continue;
            }

            object? inputObject;
            try
            {
                // Try to start and queue
                inputObject = workitem.Queue(invokable.InputType);
                await context.SaveChangesAsync();
            }
            catch (DBConcurrencyException)
            {
                // Some other process managed to snatch the task
                continue;
            }

            // The workitem is sent to different thread, so detach here
            context.Entry(workitem).State = EntityState.Detached;

            queue
                .Enqueue(
                    async (CancellationToken cancellationToken) =>
                    {
                        // This is run in it's own thread, and needs a new scope
                        using var scope = services.CreateScope();
                        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();

                        // Attaches the persistent job to this context instead
                        context.Attach(workitem);

                        try
                        {
                            // Invoke and complete
                            var outputObject = await invokable.Invoke(
                                inputObject,
                                services,
                                cancellationToken
                            );
                            workitem.Complete(outputObject);
                            await context.SaveChangesAsync(CancellationToken.None);
                        }
                        catch (TargetInvocationException te)
                        {
                            await workitem.InsertException(context, te.InnerException);
                            await context.SaveChangesAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            await workitem.InsertException(context, ex);
                            await context.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                )
                .WithTimeLimit(workitem.GetTimeLimit());
            // .WithExceptionHandler(
            //     ex =>
            //     {
            //         Console.WriteLine(ex);
            //     }
            // );
        }
        // Awaits until the queue is completed
        await queue.Process();
    }

    internal record Invokable(
        MethodInfo Method,
        TimeSpan? TimeLimit = null,
        Type[]? ServiceTypes = null,
        Type? InputType = null,
        Type? ReturnType = null,
        bool HasCancellationToken = false
    )
    {
        async public Task<object?> Invoke(
            object? input,
            IServiceProvider? serviceProvider = null,
            CancellationToken cancellationToken = default
        )
        {
            var invokeParams = new List<object?>() { };

            if (InputType != null)
            {
                invokeParams.Add(input);
            }

            // Get service parameters
            if (serviceProvider != null && ServiceTypes != null)
            {
                var services = ServiceTypes.Select(p => serviceProvider.GetRequiredService(p));
                invokeParams.AddRange(services);
            }

            if (HasCancellationToken)
            {
                invokeParams.Add(cancellationToken);
            }

            Task outputTask = (Task)Method.Invoke(null, invokeParams.ToArray())!;
            await outputTask.ConfigureAwait(false);

            if (ReturnType != null)
            {
                // TODO: Maybe the calling function could provide accurate type here?
                return ((dynamic)outputTask).Result as object;
            }
            else
            {
                return null;
            }
        }
    }

    internal async static Task<Deferred> EnqueueCronjob(
        DbContext context,
        CronJob cronJob,
        DateTime attemptAfter
    )
    {
        if (cronJob.SchedulerInstance is null)
        {
            throw new InvalidOperationException(
                "CronJob SchedulerInstance must be set in order to enqueue"
            );
        }

        var deferredJob = DeferredJob.CreateFromCronJob(
            cronJob,
            new DeferredOptions() { AttemptAfter = attemptAfter }
        );
        return await DeferredJob.Repository.Insert(context, deferredJob);
    }

    public async static Task<Deferred> Enqueue(
        DbContext context,
        Delegate methodDelegate,
        object? input = null,
        DeferredOptions? opts = null
    )
    {
        var job = DeferredJob.CreateFromMethod(methodDelegate.GetMethodInfo(), input, opts);
        return await DeferredJob.Repository.Insert(context, job);
    }

    public async static Task<Deferred<O>> Enqueue<O>(
        DbContext context,
        Delegate methodDelegate,
        object input,
        DeferredOptions? opts = null
    )
    {
        var job = DeferredJob.CreateFromMethod(methodDelegate.GetMethodInfo(), input, opts);
        return await DeferredJob.Repository.Insert<O>(context, job);
    }

    private void BuildMethodsCache()
    {
        var methodInfos = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Select(t => (t, t.GetCustomAttribute<DeferredAttribute>()))
            .Where(ma => ma.Item2 != null);

        foreach (var (method, attribute) in methodInfos)
        {
            var key = method.Name;

            if (!method.IsStatic)
            {
                throw new InvalidOperationException("Persistent jobs work only on static methods.");
            }

            if (!method.IsPublic)
            {
                throw new InvalidOperationException($"Deferred method '{key}' needs to be public.");
            }

            if (methods.ContainsKey(key))
            {
                throw new Exception(
                    $"Persistent job methods need to be unique, job with name '{key}' is already defined."
                );
            }

            var parameters = method.GetParameters();
            var first = parameters.FirstOrDefault();
            var last = parameters.LastOrDefault();
            var taskType = method.ReturnType;
            if (taskType.Name != "Task" && taskType.Name != "Task`1")
            {
                throw new InvalidOperationException(
                    $"Deferred method '{key}' must return Task or Task<T>"
                );
            }

            Type? retType = null;
            Type? inputType = null;
            var hasCancellationToken = false;
            var skip = 0;
            var skipLast = 0;

            if (taskType.IsGenericType)
            {
                retType = taskType.GetGenericArguments().First();
            }

            if (first?.Name == "input")
            {
                inputType = parameters.First().ParameterType;
                skip = 1;
            }

            if (last?.ParameterType.FullName == "System.Threading.CancellationToken")
            {
                hasCancellationToken = true;
                skipLast = 1;
            }

            if (attribute.MaxParallelizationCount > 0)
            {
                maxParellelizationByMethod[key] = attribute.MaxParallelizationCount;
            }

            var serviceTypes = parameters
                .Skip(skip)
                .SkipLast(skipLast)
                .Select(t => t.ParameterType)
                .ToArray();

            methods[key] = new Invokable(
                method,
                InputType: inputType,
                ReturnType: retType,
                ServiceTypes: serviceTypes,
                HasCancellationToken: hasCancellationToken
            );
        }
    }
}
