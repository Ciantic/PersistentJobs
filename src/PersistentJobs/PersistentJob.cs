using System.Data;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;

public class PersistentJob
{
    public Guid Id { get; set; }

    public string AssemblyName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string? OutputJson { get; set; } = null;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Started { get; set; } = null;
    public DateTime? Ended { get; set; } = null;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    private MethodInfo GetMethodInfo()
    {
        var method = Assembly.Load(AssemblyName).GetType(ClassName)?.GetMethod(MethodName);

        if (method == null)
        {
            // TODO: This failure is pretty bad, do something to log it
            throw new Exception("Method not found");
        }

        return method;
    }

    async public void Execute(DbContext context, IServiceProvider? serviceProvider = null)
    {
        Started = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DBConcurrencyException)
        {
            return;
            // pass
        }

        var method = GetMethodInfo();
        var parameters = method.GetParameters();

        // Get input parameter
        var inputPar = parameters.First();
        var inputValue = JsonSerializer.Deserialize(InputJson, inputPar.ParameterType);
        var invokeParams = new List<object?>() { inputValue };

        // Get service parameters
        if (serviceProvider != null)
        {
            var serviceTypes = parameters.Skip(1).ToArray();
            var services = serviceTypes.Select(
                p => serviceProvider.GetRequiredService(p.ParameterType)
            );
            invokeParams.AddRange(services);
        }

        // Execute and store to OutputJson
        var outputValue = method.Invoke(null, invokeParams.ToArray());
        OutputJson = JsonSerializer.Serialize(outputValue);
        await context.SaveChangesAsync();

        Console.WriteLine("Output {0}", outputValue);
    }
}

public interface IJobRunning<Output>
{
    public Output? Completed();
    public Task CancelAsync();
}

public interface IJob<Input, Output>
{
    public Task<IJobRunning<Output>> StartAsync(Input input);
}

public class JobService : IHostedService
{
    private readonly TaskQueue Queue = new();
    private Timer _timer = null!;

    private readonly IServiceProvider _services;

    // public void Dispose()
    // {
    //     throw new NotImplementedException();
    // }

    public JobService(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO: For this to work right, one should not add anything to queue
        // after this
        Queue.Cancel();
        await Queue.Process();
    }

    private async void DoWork(object? state)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();
        var unstarted = context.Set<PersistentJob>().Where(p => p.Started == null).First();
    }

    private static void Methods(Assembly assembly)
    {
        Dictionary<string, MethodInfo> methods = assembly
            .GetTypes()
            .SelectMany(x => x.GetMethods())
            .Where(y => y.GetCustomAttributes().OfType<JobAttribute>().Any())
            .ToDictionary(z => z.Name);
    }
}

public static class ModelBuilderExtension
{
    public static void AddPersistentJobs(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersistentJob>().Property(p => p.IdempotencyKey).IsConcurrencyToken();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class JobAttribute : Attribute { }
