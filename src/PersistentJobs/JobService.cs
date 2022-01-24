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

    internal Invokable GetMethod(string methodName)
    {
        return methods[methodName];
    }

    public JobService(IServiceProvider services)
    {
        queue = new(8);
        this.services = services;

        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0);

        foreach (var method in methods)
        {
            var key = method.Name;
            if (this.methods.ContainsKey(key))
            {
                throw new Exception("Only one with same name");
            }
            var types = new List<Type>(method.GetParameters().Select(p => p.ParameterType))
            {
                method.ReturnType
            };
            var parameters = method.GetParameters();
            var inputPar = parameters.First();
            this.methods[key] = new Invokable(
                method,
                inputType: inputPar.ParameterType,
                serviceTypes: parameters.Skip(1).Select(t => t.ParameterType).ToArray()
            );
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
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
        using var scope = services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();

        var unstarted = await PersistentJob.Repository.GetUnstarted(context);

        // Try to start each work item
        foreach (var workitem in unstarted)
        {
            var invokable = methods[workitem.MethodName];
            try
            {
                // Try to start and queue
                var inputObject = await workitem.Start(context, invokable.inputType);
                queue.Queue(
                    async () =>
                    {
                        var outputObject = await invokable.Invoke(inputObject, services);
                        await workitem.Complete(context, outputObject);
                    }
                );
            }
            catch (DBConcurrencyException)
            {
                // Some other process managed to snatch the task
            }
        }

        await queue.Process();
    }

    internal record Invokable(MethodInfo method, Type inputType, Type[] serviceTypes)
    {
        async public Task<object> Invoke(object? input, IServiceProvider? serviceProvider = null)
        {
            var invokeParams = new List<object?>() { input };

            // Get service parameters
            if (serviceProvider != null)
            {
                var services = serviceTypes.Select(p => serviceProvider.GetRequiredService(p));
                invokeParams.AddRange(services);
            }

            Task outputTask = (Task)method.Invoke(null, invokeParams.ToArray())!;
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
}
