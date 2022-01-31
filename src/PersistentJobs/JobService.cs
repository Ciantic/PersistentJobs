using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentJobs;

public class JobService
{
    private readonly TaskQueue queue;
    private readonly Dictionary<string, Invokable> methods = new();
    private readonly IServiceProvider services;

    public record JobServiceOpts(int MaxParallelizationCount = 8)
    {
    }

    internal JobService(JobServiceOpts opts, IServiceProvider services)
    {
        queue = new(opts.MaxParallelizationCount);
        this.services = services;
        methods = BuildMethodsCache();
    }

    public async Task StopAsync()
    {
        queue.Cancel();
        await queue.Process();
    }

    public async Task RunAsync()
    {
        // List<PersistentJob> unstarted;
        using var scope = services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        var availableJobs = await PersistentJob.Repository.GetAvailable(context);

        // Start and queue each work item
        foreach (var workitem in availableJobs)
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
                .Queue(
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
                .WithTimeLimit(workitem.GetTimeLimit())
                .WithExceptionHandler(
                    ex =>
                    {
                        Console.WriteLine(ex);
                    }
                );
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

    public async static Task<DeferredTask> AddTask(
        DbContext context,
        Delegate method,
        object? input = null
    )
    {
        return await PersistentJob.Repository.Insert(context, method, input);
    }

    public async static Task<DeferredTask<O>> AddTask<O>(
        DbContext context,
        Delegate method,
        object input
    )
    {
        return await PersistentJob.Repository.Insert<O>(context, method, input);
    }

    private static Dictionary<string, Invokable> BuildMethodsCache()
    {
        Dictionary<string, Invokable> cache = new();

        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Select(t => (t, t.GetCustomAttributes<JobAttribute>().FirstOrDefault()))
            .Where(ma => ma.Item2 != null);

        foreach (var (method, attribute) in methods)
        {
            var key = method.Name;

            if (!method.IsStatic)
            {
                throw new Exception("Persistent jobs work only on static methods.");
            }

            if (cache.ContainsKey(key))
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
                throw new Exception("Deferred methods needs to be async");
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

            var serviceTypes = parameters
                .Skip(skip)
                .SkipLast(skipLast)
                .Select(t => t.ParameterType)
                .ToArray();

            cache[key] = new Invokable(
                method,
                InputType: inputType,
                ReturnType: retType,
                ServiceTypes: serviceTypes,
                HasCancellationToken: hasCancellationToken
            );
        }
        return cache;
    }
}
