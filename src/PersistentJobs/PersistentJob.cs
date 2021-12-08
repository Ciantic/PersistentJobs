using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;
public class PersistentJob
{
    public Guid Id { get; set; }
    public string Method { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string? OutputJson { get; set; } = null;
    public DateTime Start { get; set; } = DateTime.UtcNow;
    public DateTime? Ended { get; set; } = null;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
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
        var f = new MyGeneratedClass();
        f.Goo();
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
