using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class JobAttribute : Attribute
{
    public uint MaxAttempts { get; set; } = 1;
    public uint TimeLimitSeconds { get; set; } = 0; // 2147483 = int.MaxValue / 1000
    public uint WaitBetweenAttemptsSeconds { get; set; } = 0;
}
