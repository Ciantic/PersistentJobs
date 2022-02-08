using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronHourly : CronScheduler
{
    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext context,
        IServiceProvider services
    )
    {
        return from.AddHours(1);
    }
}
