using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronWeekly : CronScheduler
{
    public int Weeks { get; set; } = 1;

    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddDays(Math.Max(Weeks, 1) * 7);
    }
}
