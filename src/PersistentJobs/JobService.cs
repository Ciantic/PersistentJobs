using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PersistentJobs;

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
        // Timer runs the callback `DoWork` in it's own thread
        _timer = new Timer(RunAll, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO: For this to work right, one should not add anything to queue
        // after this
        Queue.Cancel();
        await Queue.Process();
    }

    private async void RunAll(object? state)
    {
        using var scope = _services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Pick one unstarted job and start it
        var unstarted = await context
            .Set<PersistentJob>()
            .Where(p => p.Started == null)
            .FirstAsync();
        unstarted?.Execute(context, _services);
    }
}
