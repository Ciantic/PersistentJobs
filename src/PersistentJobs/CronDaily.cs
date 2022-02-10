using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronDaily : CronScheduler
{
    public int Days { get; set; } = 1;

    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddDays(Math.Max(Days, 1));
    }
}
