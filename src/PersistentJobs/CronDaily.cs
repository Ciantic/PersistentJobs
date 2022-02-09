using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronDaily : CronScheduler
{
    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddDays(1);
    }
}
