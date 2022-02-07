using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class DeferredAttribute : Attribute
{
    [JsonIgnore]
    public uint MaxAttempts { get; set; } = 1;

    [JsonIgnore]
    public uint TimeLimitSeconds { get; set; } = 0;

    [JsonIgnore]
    public uint WaitBetweenAttemptsSeconds { get; set; } = 0;

    [JsonIgnore]
    virtual public uint MaxParallelizationCount { get; set; } = 0;

    public TimeSpan? TimeLimit
    {
        get { return TimeLimitSeconds > 0 ? TimeSpan.FromSeconds(TimeLimitSeconds) : null; }
    }

    public TimeSpan? WaitBetweenAttempts
    {
        get
        {
            return WaitBetweenAttemptsSeconds > 0
              ? TimeSpan.FromSeconds(WaitBetweenAttemptsSeconds)
              : null;
        }
    }
}
