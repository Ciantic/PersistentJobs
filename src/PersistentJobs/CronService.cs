using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public class CronService
{
    private readonly Dictionary<string, Type> schedulers;
    private List<CronJob>? methods = null;
    private readonly IServiceProvider services;

    internal CronService(IServiceProvider services)
    {
        this.services = services;
        schedulers = GetSchedulers();
    }

    public async Task ProcessAsync(DbContext context)
    {
        // await context.Database.BeginTransactionAsync();
        methods = await CronJob.Repository.UpdateOrCreate(
            context,
            currentJobs: methods ?? BuildMethodsCache(),
            initial: methods == null,
            schedulers: schedulers
        );

        foreach (var m in methods)
        {
            m.Schedule(context, services);
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DBConcurrencyException)
        {
            // Some other process managed to process before this
        }
        // await context.Database.CommitTransactionAsync();
    }

    private static Dictionary<string, Type> GetSchedulers()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(CronScheduler)))
            .ToDictionary(t => t.Name, t => t);
    }

    private static IEnumerable<CronJob> BuildMethodsCache()
    {
        List<CronJob> methods = new();
        var methodInfos = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Select(t => (t, t.GetCustomAttributes<CronScheduler>()))
            .Where(ma => ma.Item2.Any());

        foreach (var (method, attributes) in methodInfos)
        {
            foreach (var attr in attributes)
            {
                methods.Add(CronJob.CreateFromMethod(method, attr));
            }
        }

        return methods;
    }
}
