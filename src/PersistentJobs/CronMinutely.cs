using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronMinutely : CronScheduler
{
    public int Minutes { get; set; } = 1;

    public override DateTime? GetNextOccurrence(
        DateTime from,
        DbContext? context = null,
        IServiceProvider? services = null
    )
    {
        return from.AddMinutes(Math.Max(Minutes, 1));
    }
}
