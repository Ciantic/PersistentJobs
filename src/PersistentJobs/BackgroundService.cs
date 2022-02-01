using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualBasic;

namespace PersistentJobs;

public class BackgroundService : IHostedService
{
    private Timer? timer;

    private IServiceProvider services;

    private DeferredQueue deferredQueue;

    public BackgroundService(IServiceProvider services)
    {
        this.services = services;
        deferredQueue = new DeferredQueue(new(), services);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(Tick, null, 0, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        await deferredQueue.CancelAsync();
        // queue.Cancel();
        // await queue.Process();
    }

    private async void Tick(object? state)
    {
        await deferredQueue.ProcessAsync();
        timer!.Change(60000, Timeout.Infinite);
    }
}
