using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;

public class JobService : IHostedService
{
    private readonly TaskQueue queue;
    private readonly Dictionary<string, Invokable> methods = new();
    private Timer? timer;
    private readonly IServiceProvider services;

    public record JobServiceOpts(int MaxParallelizationCount = 8)
    {
    }

    public JobService(JobServiceOpts opts, IServiceProvider services)
    {
        // TODO: Configurable parallelization count
        queue = new(opts.MaxParallelizationCount);
        this.services = services;
        methods = BuildMethodsCache();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // TODO: For this to work right, one should not add anything to queue
        // after this
        queue.Cancel();
        await queue.Process();
    }

    private async void Tick(object? state)
    {
        await RunAsync();
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
            var invokable = methods[workitem.MethodName];
            try
            {
                // Try to start and queue
                var inputObject = await workitem.Queue(context, invokable.InputType);

                // The workitem is sent to different thread, so I detach here
                context.Entry(workitem).State = EntityState.Detached;

                queue.Queue(
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
                            await workitem.Complete(context, outputObject);
                        }
                        catch (Exception ex)
                        {
                            await workitem.Exception(context, ex);
                        }
                    }
                );
            }
            catch (DBConcurrencyException)
            {
                // Some other process managed to snatch the task
            }
        }

        // Awaits until the queue is completed
        await queue.Process();
    }

    internal record Invokable(
        MethodInfo Method,
        Type[]? ServiceTypes = null,
        Type? InputType = null,
        bool HasCancellationToken = false
    )
    {
        async public Task<object> Invoke(
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

            // TODO: Maybe the calling function could provide accurate type here?
            return ((dynamic)outputTask).Result as object;
        }
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
            .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0);

        foreach (var method in methods)
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

            var types = new List<Type>(method.GetParameters().Select(p => p.ParameterType))
            {
                method.ReturnType
            };

            var parameters = method.GetParameters();
            var first = parameters.FirstOrDefault();
            var last = parameters.LastOrDefault();

            Type? inputType = null;
            var hasCancellationToken = false;
            var skip = 0;
            var skipLast = 0;

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
                ServiceTypes: serviceTypes,
                HasCancellationToken: hasCancellationToken
            );
        }
        return cache;
    }
}
