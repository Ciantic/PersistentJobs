using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronHourly : CronScheduler
{
    public int RandomlyFrom { get; set; } = -1;
    public int RandomlyTo { get; set; } = -1;

    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddHours(1);
    }
}
