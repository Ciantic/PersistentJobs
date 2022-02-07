using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentJobs;

public class CronService
{
    private List<CronJob>? methods = null;
    private readonly IServiceProvider services;

    internal CronService(IServiceProvider services)
    {
        this.services = services;
    }

    public async Task ProcessAsync(DbContext context)
    {
        if (methods == null)
        {
            methods = await CronJob.Repository.UpdateOrCreate(context, BuildMethodsCache());
        }

        methods.ForEach(p => p.Tick());
    }

    private static IEnumerable<CronJob> BuildMethodsCache()
    {
        List<CronJob> methods = new();
        var methodInfos = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Select(t => (t, t.GetCustomAttributes<CronAttribute>()))
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
