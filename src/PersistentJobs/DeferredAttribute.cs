using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class DeferredAttribute : Attribute
{
    public uint MaxAttempts { get; set; } = 1;

    public uint TimeLimitSeconds { get; set; } = 0;

    public uint WaitBetweenAttemptsSeconds { get; set; } = 0;

    public uint MaxParallelizationCount { get; set; } = 0;

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
