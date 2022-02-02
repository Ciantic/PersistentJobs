using System.Reflection;

namespace PersistentJobs;

public class CronService
{
    private readonly IServiceProvider services;
    private readonly Dictionary<string, MethodInfo> methods = new();

    internal CronService(IServiceProvider services)
    {
        this.services = services;
    }

    private void BuildMethodsCache()
    {
        var methodInfos = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(t => t.GetTypes())
            .SelectMany(t => t.GetMethods())
            .Select(t => (t, t.GetCustomAttribute<CronAttribute>()))
            .Where(ma => ma.Item2 != null);


    }
}
