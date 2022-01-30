using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronJobAttribute : Attribute
{
    public int Minute { get; set; } = -1;
    public int Hour { get; set; } = -1;
    public int DayOfMonth { get; set; } = -1;
    public int Month { get; set; } = -1;
    public int DayOfWeek { get; set; } = -1;
}
