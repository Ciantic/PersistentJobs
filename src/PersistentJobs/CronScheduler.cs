using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
abstract public class CronScheduler : DeferredAttribute
{
    public abstract DateTime? GetNextOccurrence(DateTime from);

    [JsonIgnore]
    override public uint MaxParallelizationCount { get; set; } = 1;

    internal string? Serialize()
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

[AttributeUsage(AttributeTargets.Method)]
public class CronHourly : CronScheduler
{
    public int Minute { get; set; } = 0;

    public override DateTime? GetNextOccurrence(DateTime from)
    {
        return DateTime.UtcNow;
    }
}