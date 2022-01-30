using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class JobAttribute : Attribute
{
    public uint MaxAttempts { get; set; } = 1;
    public uint TimeLimitSeconds { get; set; } = 0;
    public uint WaitBetweenAttemptsSeconds { get; set; } = 0;
}
