using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;

public class JobService : IHostedService
{
    private readonly TaskQueue _queue;
    private readonly Dictionary<string, Delegate> _methods = new();
    private Timer? _timer;
    private readonly IServiceProvider _services;

    internal MethodInfo GetMethod(string methodName)
    {
        return _methods[methodName].GetMethodInfo();
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
            var dell = Delegate.CreateDelegate(Expression.GetFuncType(types.ToArray()), method);
            _methods[key] = dell;
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
                await workitem.Start(context);
                var method = _methods[workitem.MethodName].GetMethodInfo();
                _queue.Queue(
                    () =>
                    {
                        return workitem.Execute(context, method, _services);
                    }
                );
            }
            catch (DBConcurrencyException) { }
        }

        await _queue.Process();
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
