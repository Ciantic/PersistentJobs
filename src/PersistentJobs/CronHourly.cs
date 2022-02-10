using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronHourly : CronScheduler
{
    /// <summary>
    /// Hourly intervals
    /// </summary>
    public int Hours { get; set; } = 1;

    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddHours(Math.Max(Hours, 1));
    }
}
