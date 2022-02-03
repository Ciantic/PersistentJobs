using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentJobs;

public class CronService
{
    private readonly IServiceProvider services;
    private readonly Dictionary<string, (MethodInfo, CronAttribute)> methods = new();

    internal CronService(IServiceProvider services)
    {
        this.services = services;
        BuildMethodsCache();
    }

    public async Task StartAsync()
    {
        using var scope = services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DbContext>();
    }

    public async Task ProcessAsync(DbContext context)
    {
        // context.
    }

    private void BuildMethodsCache()
    {
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
                methods[method.Name] = (method, attr);
            }
        }
    }
}
