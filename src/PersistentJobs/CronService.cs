using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

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
        methods = await CronJob.Repository.UpdateOrCreate(
            context,
            methods ?? BuildMethodsCache(),
            methods == null,
            schedulers
        );

        foreach (var m in methods)
        {
            await m.Schedule(context);
        }
        await context.SaveChangesAsync();
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