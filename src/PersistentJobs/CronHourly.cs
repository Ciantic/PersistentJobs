using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronHourly : CronScheduler
{
    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddHours(1);
    }
}
