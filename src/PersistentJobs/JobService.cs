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
    private readonly TaskQueue Queue = new();
    private readonly Dictionary<string, Delegate> _methods = new Dictionary<string, Delegate>();
    private Timer _timer = null!;

    private readonly IServiceProvider _services;

    // public void Dispose()
    // {
    //     throw new NotImplementedException();
    // }

    internal MethodInfo GetMethod(string methodName)
    {
        return _methods[methodName].GetMethodInfo();
    }

    public JobService(IServiceProvider services) // TODO param Assembly[]
    {
        var methods = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(IsDeferredAttribute), false).Length > 0)
            .ToArray();
        foreach (var method in methods)
        {
            var key = method.Name.Replace("Deferred", "");
            if (_methods.ContainsKey(key))
            {
                throw new Exception("Only one with same name");
            }
            var types = new List<Type>(method.GetParameters().Select(p => p.ParameterType))
            {
                method.ReturnType
            };
            var dell = Delegate.CreateDelegate(Expression.GetFuncType(types.ToArray()), method);
            // var dell = Delegate.CreateDelegate(Expression.GetDelegateType(types.ToArray()), method);
            _methods[key] = dell;
        }
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Timer runs the callback `DoWork` in it's own thread
        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO: For this to work right, one should not add anything to queue
        // after this
        Queue.Cancel();
        await Queue.Process();
    }

    private async void Tick(object? state)
    {
        await RunAsync();
    }

    public async Task RunAsync()
    {
        using var scope = _services.CreateScope();
        // TODO: Life time of context?
        var context = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Pick one unstarted job and start it
        var unstarted = await context
            .Set<PersistentJob>()
            .Where(p => p.Started == null)
            .FirstAsync();
        var method = _methods[unstarted.MethodName].GetMethodInfo();
        unstarted?.Execute(context, method, _services);
    }
}
