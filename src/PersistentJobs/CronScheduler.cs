using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
abstract public class CronScheduler : DeferredAttribute
{
    public abstract DateTime? GetNextOccurrence(
        DateTime from,
        DbContext context,
        IServiceProvider services
    );

    [JsonIgnore]
    override public uint MaxParallelizationCount { get; set; } = 1;

    internal string Serialize()
    {
        return JsonSerializer.Serialize(
            this,
            GetType(),
            new JsonSerializerOptions { IgnoreReadOnlyProperties = true }
        );
    }

    internal string GetName()
    {
        return GetType().Name;
    }
}
