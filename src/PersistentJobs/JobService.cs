using System.Data;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;

public class JobService : IHostedService
{
    private readonly TaskQueue _queue;
    private readonly Dictionary<string, Invokable> _methods = new();
    private Timer? _timer;
    private readonly IServiceProvider _services;

    internal Invokable GetMethod(string methodName)
    {
        return _methods[methodName];
    }

    public JobService(IServiceProvider services)
    {
        _queue = new(8);
        _services = services;

        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(CreateDeferredAttribute), false).Length > 0);

        foreach (var method in methods)
        {
            var key = method.Name;
            if (_methods.ContainsKey(key))
            {
                throw new Exception("Only one with same name");
            }
            var types = new List<Type>(method.GetParameters().Select(p => p.ParameterType))
            {
                method.ReturnType
            };
            // var del = Delegate.CreateDelegate(Expression.GetFuncType(types.ToArray()), method);
            var parameters = method.GetParameters();
            var inputPar = parameters.First();
            _methods[key] = new Invokable()
            {
                method = method,
                inputType = inputPar.ParameterType,
                serviceTypes = parameters.Skip(1).Select(t => t.ParameterType).ToArray()
            };
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO: For this to work right, one should not add anything to queue
        // after this
        _queue.Cancel();
        await _queue.Process();
    }

    private async void Tick(object? state)
    {
        await RunAsync();
    }

    public async Task RunAsync()
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();

        var unstarted = await PersistentJob.Repository.GetUnstarted(context);

        // Try to start each work item
        foreach (var workitem in unstarted)
        {
            try
            {
                // Try to start
                var inputJson = await workitem.Start(context);

                // Queue the task
                var invokable = _methods[workitem.MethodName];
                _queue.Queue(
                    async () =>
                    {
                        var inputObject = JsonSerializer.Deserialize(
                            inputJson,
                            invokable.inputType
                        );
                        var outputObject = await invokable.Invoke(inputObject, _services);
                        await workitem.Complete(context, outputObject);
                    }
                );
            }
            catch (DBConcurrencyException)
            {
                // Some other process managed to snatch the task
            }
        }

        await _queue.Process();
    }

    internal record Invokable
    {
        // internal Delegate del;

        internal MethodInfo method;
        internal Type inputType;
        internal Type[] serviceTypes;

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

    internal Invokable GetInvokable()
    {
        var a = new Invokable() { };
        throw new NotImplementedException();
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
